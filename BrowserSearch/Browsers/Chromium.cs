using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using Wox.Infrastructure;
using Wox.Plugin;
using Wox.Plugin.Logger;
using BrowserInfo = Wox.Plugin.Common.DefaultBrowserInfo;

namespace BrowserSearch.Browsers
{
    internal class Chromium : IBrowser
    {
        protected string[] UserDataDirCandidates { get; }
        protected Dictionary<string, ChromiumProfile> Profiles { get; } = [];
        private readonly string? _selectedProfileName;
        private readonly List<Result> _history = [];
        // Key is query, Value is a list of predictions for that query
        private readonly Dictionary<string, List<ChromiumPrediction>> _predictions = [];

        public Chromium(string[] userDataDirCandidates, string? profileName)
        {
            UserDataDirCandidates = userDataDirCandidates;
            _selectedProfileName = profileName;
        }

        void IBrowser.Init()
        {
            CreateProfiles();

            // Load history from all profiles
            if (_selectedProfileName is null)
            {
                foreach (ChromiumProfile profile in Profiles.Values)
                {
                    profile.Init(_history, _predictions);
                }

                return;
            }

            // Load history from selected profile
            if (!Profiles.TryGetValue(_selectedProfileName.ToLower(), out ChromiumProfile? selectedProfile))
            {
                Log.Error($"Couldn't find profile '{_selectedProfileName}'", typeof(Chromium));
                MessageBox.Show($"No profile with the name '{_selectedProfileName}' was found.", "BrowserSearch");

                return;
            }
            selectedProfile.Init(_history, _predictions);
        }

        protected virtual void CreateProfiles()
        {
            string? userDataDir = null;
            foreach (string candidate in UserDataDirCandidates)
            {
                if (Directory.Exists(candidate))
                {
                    userDataDir = candidate;
                    break;
                }
            }

            if (userDataDir is null)
                throw new NullReferenceException("Couldn\'t find UserData directory");

            Log.Info($"Found UserData directory: {userDataDir}", typeof(Chromium));
            using StreamReader jsonFileReader = new(
                new FileStream(Path.Join(userDataDir, "Local State"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            );

            JsonDocument localState = JsonDocument.Parse(jsonFileReader.ReadToEnd());
            jsonFileReader.Close();

            string[] nameProperties = ["gaia_given_name", "gaia_name", "name", "shortcut_name"];
            JsonElement infoCache = localState.RootElement.GetProperty("profile").GetProperty("info_cache");
            foreach (JsonProperty profileInfo in infoCache.EnumerateObject())
            {
                ChromiumProfile profile = new(Path.Join(userDataDir, profileInfo.Name));
                Profiles[profileInfo.Name.ToLower()] = profile;

                foreach (string nameProp in nameProperties)
                {
                    if (profileInfo.Value.TryGetProperty(nameProp, out JsonElement nameElem))
                    {
                        string? name = nameElem.GetString()?.ToLower();
                        if (!string.IsNullOrEmpty(name))
                        {
                            Profiles[name] = profile;
                        }
                    }
                }
            }
        }

        List<Result> IBrowser.GetHistory()
        {
            return _history;
        }

        public int CalculateExtraScore(string query, string title, string url)
        {
            if (!_predictions.TryGetValue(query, out List<ChromiumPrediction>? predictions))
            {
                return 0;
            }

            foreach (ChromiumPrediction prediction in predictions)
            {
                if (prediction.url == url)
                {
                    return (int)prediction.hits;
                }
            }

            return 0;
        }
    }

    internal class ChromiumPrediction(string url, long hits)
    {
        public readonly string url = url;
        public readonly long hits = hits;
    }

    internal class ChromiumProfile
    {
        private readonly string _path;
        private bool _initialized;
        private SqliteConnection? _historyDbConnection, _predictorDbConnection;

        public ChromiumProfile(string path)
        {
            _path = path;
        }

        public void Init(List<Result> history, Dictionary<string, List<ChromiumPrediction>> predictions)
        {
            if (_initialized)
            {
                return;
            }
            Log.Info($"Initializing Chromium profile: '{_path}'", typeof(ChromiumProfile));

            try
            {
                CopyDatabases();
            }
            catch (FileNotFoundException)
            {
                Log.Warn($"Couldn't find database files in '{_path}'", typeof(ChromiumProfile));
                return;
            }
            ArgumentNullException.ThrowIfNull(_historyDbConnection);
            ArgumentNullException.ThrowIfNull(_predictorDbConnection);

            PopulatePredictions(predictions);
            PopulateHistory(history);

            _historyDbConnection.Close();
            _predictorDbConnection.Close();
            _historyDbConnection.Dispose();
            _predictorDbConnection.Dispose();
            _initialized = true;
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

        public void PopulatePredictions(Dictionary<string, List<ChromiumPrediction>> predictions)
        {
            ArgumentNullException.ThrowIfNull(_predictorDbConnection);

            using SqliteCommand cmd = new("SELECT user_text, url, number_of_hits FROM network_action_predictor");
            using SqliteDataReader reader = ExecuteCmd(_predictorDbConnection, cmd);
            while (reader.Read())
            {
                string query = (string)reader[0];
                string url = (string)reader[1]; // Predicted URL for that query
                long hits = (long)reader[2]; // Amount of times the prediction was correct and the user selected it

                if (!predictions.TryGetValue(query, out List<ChromiumPrediction>? value))
                {
                    value = [];
                    predictions[query] = value;
                }

                value.Add(new ChromiumPrediction(url, hits));
            }
        }

        public void PopulateHistory(List<Result> history)
        {
            ArgumentNullException.ThrowIfNull(_historyDbConnection);

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
                            Log.Error($"Couldn't open '{url}'", typeof(ChromiumProfile));
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
