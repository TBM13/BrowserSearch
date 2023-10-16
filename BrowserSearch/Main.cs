using BrowserSearch;
using System;
using System.Collections.Generic;
using System.Linq;
using Wox.Plugin;
using Wox.Plugin.Logger;
using BrowserInfo = Wox.Plugin.Common.DefaultBrowserInfo;

namespace Community.Powertoys.Run.Plugin.BrowserSearch
{
    public class Main : IPlugin, IReloadable
    {
        public static string PluginID => "E5A9FC7A3F7F4320BE612DA95C57C32D";
        public string Name => "Browser Search";
        public string Description => "Search in your browser's history.";

        private PluginInitContext? _context;
        private IBrowser? _defaultBrowser;
        private long _lastUpdateTickCount = -300L;

        public void Init(PluginInitContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            InitDefaultBrowser();
        }

        public void ReloadData()
        {
            if (_context is null)
            {
                return;
            }

            // When the plugin is disabled and then re-enabled,
            // ReloadData() is called multiple times so this is needed
            long tickCount = Environment.TickCount64;
            if (tickCount - _lastUpdateTickCount >= 300)
            {
                _lastUpdateTickCount = tickCount;
                InitDefaultBrowser();
            }
        }

        private void InitDefaultBrowser()
        {
            // Retrieve default browser info
            BrowserInfo.UpdateIfTimePassed();

            _defaultBrowser = null;
            switch (BrowserInfo.Name)
            {
                case "Google Chrome":
                    _defaultBrowser = new Chrome();
                    break;
                default:
                    Log.Error($"Unsupported/unrecognized default browser '{BrowserInfo.Name}'", typeof(Main));
                    return;
            }

            Log.Info($"Initializing browser '{BrowserInfo.Name}'", typeof(Main));
            _defaultBrowser.Init();
        }

        public List<Result> Query(Query query)
        {
            if (query is null)
            {
                throw new ArgumentNullException(nameof(query));
            }
            if (_defaultBrowser is null)
            {
                return new List<Result>();
            }

            List<Result> history = _defaultBrowser.GetHistory();
            // This happens when the user only typed this plugin's ActionKeyword ("b?")
            if (string.IsNullOrEmpty(query.Search))
            {
                return history;
            }    

            List<Result> results = new();
            // Get the Browser's prediction for this specific query, if it has one
            _defaultBrowser.Predictions.TryGetValue(query.Search, out string? prediction);

            for (int i = 0; i < history.Count; i++)
            {
                Result r = history[i];

                int score = CalculateScore(query.Search, r.Title, r.SubTitle, prediction);
                if (score <= 0)
                {
                    continue;
                }    

                r.Score = score;
                results.Add(r);
            }

            return results;
        }

        private int CalculateScore(string query, string title, string url, string? predictionUrl)
        {
            // Since PT Run's FuzzySearch is too slow, and the history usually has a lot of entries,
            // lets calculate the scores manually.
            float titleScore = title.Contains(query, StringComparison.InvariantCultureIgnoreCase)
                ? ((float)query.Length / (float)title.Length * 100f)
                : 0;
            float urlScore = url.Contains(query, StringComparison.InvariantCultureIgnoreCase)
                ? ((float)query.Length / (float)url.Length * 100f)
                : 0;

            float score = new[] { titleScore, urlScore }.Max();
            // If the browser has a prediction for this specific query,
            // we want to give a higher score to the entry that has
            // the prediction's url
            if (predictionUrl is not null && predictionUrl == url)
            {
                score += 50;
            }

            return (int)score;
        }
    }
}
