using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using Wox.Infrastructure;
using Wox.Plugin;
using Wox.Plugin.Logger;
using BrowserInfo = Wox.Plugin.Common.DefaultBrowserInfo;

namespace BrowserSearch
{
    internal class Chromium : IBrowser
    {
        public Dictionary<string, string> Predictions { get; } = new();
        private readonly List<Result> _history = new();
        private readonly string _userDataDir;

        public Chromium(string userDataDir)
        {
            _userDataDir = userDataDir;
        }

        void IBrowser.Init()
        {
            foreach (string path in Directory.GetDirectories(_userDataDir))
            {
                if (path.EndsWith("System Profile") || path.EndsWith("Guest Profile"))
                {
                    continue;
                }    

                if (path.EndsWith("Default") || path.Contains("Profile"))
                {
                    ChromiumProfile profile = new(path);
                    profile.Init(_history, Predictions);
                }
            }
        }

        List<Result> IBrowser.GetHistory()
        {
            return _history ?? new List<Result>();
        }
    }

    internal class ChromiumProfile
    {
        private readonly string _path;
        private SqliteConnection? _historyDbConnection, _predictorDbConnection;

        public ChromiumProfile(string path)
        {
            _path = path;
        }

        public void Init(List<Result> history, Dictionary<string, string> predictions)
        {
            Log.Info($"Initializing Chromium profile: '{_path}'", typeof(Chromium));

            try
            {
                CopyDatabases();
            }
            catch (FileNotFoundException)
            {
                Log.Warn($"Couldn't find database files in '{_path}'", typeof(Chromium));
                return;
            }
            if (_historyDbConnection is null || _predictorDbConnection is null)
            {
                throw new NullReferenceException(nameof(_historyDbConnection));
            }
            if (_predictorDbConnection is null)
            {
                throw new NullReferenceException(nameof(_predictorDbConnection));
            }

            PopulatePredictions(predictions);
            PopulateHistory(history);

            _historyDbConnection.Close();
            _predictorDbConnection.Close();
            _historyDbConnection.Dispose();
            _predictorDbConnection.Dispose();
        }

        private void CopyDatabases()
        {
            string _dirName = _path[(_path.LastIndexOf('\\') + 1)..];
            string historyCopy = Path.GetTempPath() + @"\BrowserSearch_History_" + _dirName;
            string predictorCopy = Path.GetTempPath() + @"\BrowserSearch_ActionPredictor_" + _dirName;

            // We need to copy the databases, otherwise we can't open them while the browser is running
            File.Copy(
                Path.Join(_path, @"\History"), historyCopy, true
            );
            File.Copy(
                Path.Join(_path, @"\Network Action Predictor"), predictorCopy, true
            );

            _historyDbConnection = new($"Data Source={historyCopy}");
            _predictorDbConnection = new($"Data Source={predictorCopy}");
        }

        private static SqliteDataReader ExecuteCmd(SqliteConnection connection, SqliteCommand cmd)
        {
            cmd.Connection = connection;
            connection.Open();

            return cmd.ExecuteReader();
        }

        public void PopulatePredictions(Dictionary<string, string> predictions)
        {
            if (_predictorDbConnection is null)
            {
                throw new NullReferenceException(nameof(_predictorDbConnection));
            }

            Dictionary<string, long> _predictionHits = new();
            using SqliteCommand cmd = new("SELECT user_text, url, number_of_hits FROM network_action_predictor");
            using SqliteDataReader reader = ExecuteCmd(_predictorDbConnection, cmd);
            while (reader.Read())
            {
                string text = (string)reader[0]; // Query
                string url = (string)reader[1]; // Predicted URL for that query
                long hits = (long)reader[2]; // Amount of times the prediction was correct and the user selected it

                // There can be multiples predictions for the same query
                // So lets make sure to only select the one with the most hits
                if (_predictionHits.GetValueOrDefault(text, -1) < hits)
                {
                    _predictionHits[text] = hits;
                    predictions[text] = url;
                }
            }
        }

        public void PopulateHistory(List<Result> history)
        {
            if (_historyDbConnection is null)
            {
                throw new NullReferenceException(nameof(_historyDbConnection));
            }

            using SqliteCommand historyReadCmd = new("SELECT url, title FROM urls ORDER BY visit_count DESC");
            using SqliteDataReader reader = ExecuteCmd(_historyDbConnection, historyReadCmd);
            while (reader.Read())
            {
                string url = (string)reader[0];
                string title = (string)reader[1];

                Result result = new()
                {
                    QueryTextDisplay = url,
                    Title = title,
                    SubTitle = url,
                    IcoPath = BrowserInfo.IconPath,
                    Action = action =>
                    {
                        // Open URL in default browser
                        if (!Helper.OpenInShell(url))
                        {
                            Log.Error($"Couldn't open '{url}'", typeof(Chromium));
                            return false;
                        }

                        return true;
                    },
                };

                history.Add(result);
            }
        }
    }
}
