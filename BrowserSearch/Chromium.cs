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

namespace BrowserSearch
{
    internal class Chromium : IBrowser
    {
        private readonly string _userDataDir;
        private readonly Dictionary<string, ChromiumProfile> _profiles = new();
        private readonly string? _selectedProfileName;
        private readonly List<Result> _history = new();
        private readonly Dictionary<string, List<ChromiumPrediction>> _predictions = new();

        public Chromium(string userDataDir, string? profileName)
        {
            _userDataDir = userDataDir;
            _selectedProfileName = profileName;
        }

        void IBrowser.Init()
        {
            CreateProfiles();

            // Load history from all profiles
            if (_selectedProfileName is null)
            {
                foreach (ChromiumProfile profile in _profiles.Values)
                {
                    profile.Init(_history, _predictions);
                }

                return;
            }

            // Load history from selected profile
            if (!_profiles.TryGetValue(_selectedProfileName.ToLower(), out ChromiumProfile? selectedProfile))
            {
                Log.Error($"Couldn't find profile '{_selectedProfileName}'", typeof(Chromium));
                MessageBox.Show($"No profile with the name '{_selectedProfileName}' was found.", "BrowserSearch");

                return;
            }
            selectedProfile.Init(_history, _predictions);
        }

        private void CreateProfiles()
        {
            // Determine the appropriate path for the Local State file
            string localStatePath = GetLocalStatePath(_userDataDir);

            using StreamReader jsonFileReader = new(new FileStream(localStatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            JsonDocument localState = JsonDocument.Parse(jsonFileReader.ReadToEnd());

            string[] nameProperties = { "gaia_given_name", "gaia_name", "name", "shortcut_name" };
            JsonElement infoCache = localState.RootElement.GetProperty("profile").GetProperty("info_cache");
            foreach (JsonProperty profileInfo in infoCache.EnumerateObject())
            {
                ChromiumProfile profile = new(Path.Join(_userDataDir, profileInfo.Name));
                _profiles[profileInfo.Name.ToLower()] = profile;

                foreach (string nameProp in nameProperties)
                {
                    if (profileInfo.Value.TryGetProperty(nameProp, out JsonElement nameElem))
                    {
                        string? name = nameElem.GetString()?.ToLower();
                        if (!string.IsNullOrEmpty(name))
                        {
                            _profiles[name] = profile;
                        }
                    }
                }
            }
        }

        private string GetLocalStatePath(string userDataDir)
        {
            // Check if Opera GX is used, otherwise use the default path
            // Adjust the condition to identify Opera GX or other browsers
            if (IsOperaGX(userDataDir))
            {
                return Path.Join(userDataDir, @"Opera GX Stable\Local State");
            }
            else
            {
                return Path.Join(userDataDir, "Local State");
            }
        }

        private bool IsOperaGX(string userDataDir)
        {
            // Implement a method to identify if the browser is Opera GX
            // For example, check for specific files or directory names
            return Directory.Exists(Path.Join(userDataDir, "Opera GX Stable"));
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

    internal class ChromiumPrediction
    {
        public readonly string url;
        public readonly long hits;

        public ChromiumPrediction(string url, long hits)
        {
            this.url = url;
            this.hits = hits;
        }
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
            string _dirName = _path.Substring(_path.LastIndexOf('\\') + 1);
            string historyCopy = Path.GetTempPath() + @"\BrowserSearch_History_" + _dirName;
            string predictorCopy = Path.GetTempPath() + @"\BrowserSearch_ActionPredictor_" + _dirName;

            // We need to copy the databases, otherwise we can't open them while the browser is running
            File.Copy(
                Path.Join(_path, "History"), historyCopy, true
            );
            File.Copy(
                Path.Join(_path, "Network Action Predictor"), predictorCopy, true
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
                    value = new List<ChromiumPrediction>();
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
