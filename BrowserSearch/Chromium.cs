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
        private SqliteConnection? _historyDbConnection, _predictorDbConnection;
        private readonly string _userDataPath;

        public Chromium(string userDataPath)
        {
            _userDataPath = userDataPath;
        }

        void IBrowser.Init()
        {
            CopyDatabases();
            if (_historyDbConnection is null || _predictorDbConnection is null)
            {
                throw new ArgumentNullException(nameof(_historyDbConnection));
            }
            if (_predictorDbConnection is null)
            {
                throw new ArgumentNullException(nameof(_predictorDbConnection));
            }

            PopulatePredictions();
            PopulateHistory();

            _historyDbConnection.Close();
            _predictorDbConnection.Close();
        }

        private void CopyDatabases()
        {
            string historyCopy = Path.GetTempPath() + @"\BrowserSearch_History";
            string predictorCopy = Path.GetTempPath() + @"\BrowserSearch_ActionPredictor";

            // We need to copy the databases. If we don't, we won't be able to open them
            // while the browser is running
            File.Copy(
                Path.Join(_userDataPath, @"Default\History"),
                historyCopy, true
            );
            File.Copy(
                Path.Join(_userDataPath, @"Default\Network Action Predictor"),
                predictorCopy, true
            );

            _historyDbConnection = new($"Data Source={historyCopy}");
            _predictorDbConnection = new($"Data Source={predictorCopy}");
        }

        private SqliteDataReader ExecuteCmd(SqliteConnection connection, SqliteCommand cmd)
        {
            cmd.Connection = connection;
            connection.Open();

            return cmd.ExecuteReader();
        }

        private void PopulatePredictions()
        {
            if (_predictorDbConnection is null)
            {
                throw new ArgumentNullException(nameof(_predictorDbConnection));
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
                    Predictions[text] = url;
                }
            }
        }

        private void PopulateHistory()
        {
            if (_historyDbConnection is null)
            {
                throw new ArgumentNullException(nameof(_historyDbConnection));
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

                _history.Add(result);
            }
        }

        List<Result> IBrowser.GetHistory()
        {
            return _history ?? new List<Result>();
        }
    }
}
