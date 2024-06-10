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
    internal class Firefox : IBrowser
    {
        private readonly string _userDataDir;
        private readonly Dictionary<string, FirefoxProfile> _profiles = [];
        private readonly string? _selectedProfileName;
        private readonly List<Result> _history = [];
        private readonly Dictionary<(string, string), long> _frecencyValues = new();

        public Firefox(string userDataDir, string? profileName)
        {
            _userDataDir = userDataDir;
            _selectedProfileName = profileName;
        }

        void IBrowser.Init()
        {
            CreateProfiles();

            Log.Info($"Loading firefox profile '{_selectedProfileName}'", typeof(Firefox));

            // Load history from all profiles
            if (_selectedProfileName is null)
            {
                foreach (FirefoxProfile profile in _profiles.Values)
                {
                    profile.Init(_history, _frecencyValues);
                }

                return;
            }

            // Load history from selected profile
            if (!_profiles.TryGetValue(_selectedProfileName.ToLower(), out FirefoxProfile? selectedProfile))
            {
                Log.Error($"Couldn't find profile '{_selectedProfileName}'", typeof(Firefox));
                MessageBox.Show($"No profile with the name '{_selectedProfileName}' was found.", "BrowserSearch");

                return;
            }
            selectedProfile.Init(_history, _frecencyValues);
        }

        private void CreateProfiles()
        {

            // _userDataDir contains the path to the ...Roaming\Mozilla\Firefox directory

            // Inside the user data directory, there is a directory called Profiles.
            // Inside the Profiles directory, there are directories for each profile.
            // The directory are named with the following format: <random_string>.<profile_name>

            string profilesDir = Path.Join(_userDataDir, "Profiles");
            string[] profileDirectories = Directory.GetDirectories(profilesDir);

            foreach (string profileDir in profileDirectories)
            {
                string profileName = Path.GetFileName(profileDir);
                int dotIndex = profileName.IndexOf('.');
                if (dotIndex != -1)
                {
                    profileName = profileName.Substring(dotIndex + 1);
                }
                Log.Info($"Found profile: '{profileName}'", typeof(Firefox));
                _profiles[profileName.ToLower()] = new FirefoxProfile(profileDir);
            }
        }

        List<Result> IBrowser.GetHistory()
        {
            return _history;
        }

        public int CalculateExtraScore(string query, string title, string url)
        {
            // The history entries are stored in the _history list
            // Those entries have a frecency value
            // The frecency value is a combination of the amount of times the user visited the page and the time since the last visit

            if (url.Contains(query, StringComparison.InvariantCultureIgnoreCase) || title.Contains(query, StringComparison.InvariantCultureIgnoreCase))
            {
                long frecency = _frecencyValues.GetValueOrDefault((url, title), 0);
                return (int)frecency / 1000;
            }
            else
            {
                return 0;
            }
        }
    }

    internal class FirefoxProfile
    {
        private readonly string _path;
        private bool _initialized;
        private SqliteConnection? _historyDbConnection;

        public FirefoxProfile(string path)
        {
            _path = path;
        }

        public void Init(List<Result> history, Dictionary<(string, string), long> frecencyValues)
        {
            if (_initialized)
            {
                return;
            }
            Log.Info($"Initializing Firefox profile: '{_path}'", typeof(FirefoxProfile));

            try
            {
                CopyDatabases();
            }
            catch (FileNotFoundException)
            {
                Log.Warn($"Couldn't find database file in '{_path}'", typeof(FirefoxProfile));
                return;
            }
            ArgumentNullException.ThrowIfNull(_historyDbConnection);

            PopulateHistory(history, frecencyValues);


            _historyDbConnection.Close();
            _historyDbConnection.Dispose();
            _initialized = true;

            Log.Info($"Finished initializing Firefox profile: '{_path}'", typeof(FirefoxProfile));
        }

        private void CopyDatabases()
        {
            string _dirName = _path[(_path.LastIndexOf('\\') + 1)..];
            string historyCopy = Path.GetTempPath() + @"\BrowserSearch_History_" + _dirName;

            File.Copy(
                Path.Join(_path, @"\places.sqlite"), historyCopy, true
            );

            _historyDbConnection = new($"Data Source={historyCopy}");
        }

        private static SqliteDataReader ExecuteCmd(SqliteConnection connection, SqliteCommand cmd)
        {
            cmd.Connection = connection;
            connection.Open();

            return cmd.ExecuteReader();
        }

        public void PopulateHistory(List<Result> history, Dictionary<(string, string), long> frecencyValues)
        {
            ArgumentNullException.ThrowIfNull(_historyDbConnection);

            // Read the history entries from the database
            using SqliteCommand historyReadCmd = new("SELECT url, title, frecency FROM moz_places GROUP BY url ORDER BY frecency DESC"); // Limiting here is possible
            using SqliteDataReader reader = ExecuteCmd(_historyDbConnection, historyReadCmd);

            // Iterate over the sql results
            while (reader.Read())
            {
                // Make sure there is no System.DBNull value
                if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2))
                {
                    continue;
                }


                string url = (string)reader[0];
                string title = (string)reader[1];
                long frecency = (long)reader[2];

                // Add the frecency value to the frecencyValues dictionary
                frecencyValues[(url, title)] = frecency;


                // Create a new Wox Result object and add it to the history list
                Result result = new()
                {
                    QueryTextDisplay = url, // The text that will be displayed in the search box
                    Title = title, // The title of the result
                    SubTitle = url, // The subtitle of the result
                    IcoPath = BrowserInfo.IconPath, // The icon that will be displayed next to the result
                    Action = action => // The action that will be executed when the result is selected
                    {
                        // Open URL in default browser
                        if (!Helper.OpenInShell(url))
                        {
                            Log.Error($"Couldn't open '{url}'", typeof(FirefoxProfile));
                            return false;
                        }

                        return true;
                    },
                };

                history.Add(result);
            }
            history.Reverse(); // Reversing puts the highest frecency values to the end
            // This way, the highest frecency values will be the first to be displayed in the search results when the user input is vague
        }
    }
}
