using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TrophyPrompt.Core;
using TrophyPrompt.ViewModels;

namespace TrophyPrompt.Views
{
    // Avalonia port of legacy CopyFrom: scrape timestamps via FlareSolverr,
    // then either copy them verbatim or shift them (smart copy).
    public partial class CopyFromDialog : Window
    {
        private sealed class Pair
        {
            public int Id;
            public long Date;
            public Pair(int id, long date)
            {
                Id = id;
                Date = date;
            }
        }

        private CopyFromDialogViewModel Vm
        {
            get { return (CopyFromDialogViewModel)DataContext; }
        }

        public CopyFromDialog()
        {
            InitializeComponent();
            DataContext = new CopyFromDialogViewModel();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close((List<long>)null);
        }

        private async void OnOkClick(object sender, RoutedEventArgs e)
        {
            if (!Vm.TryValidate())
            {
                return;
            }
            Vm.IsBusy = true;
            Vm.Status = "Contacting FlareSolverr (localhost:8191)...";
            Vm.Error = string.Empty;
            try
            {
                List<Pair> pairs = await Task.Run(() => Scrape(Vm.Url));
                List<long> times = Vm.UseSmartCopy ? SmartCopy(pairs, Vm).ToList() : pairs.Select(p => p.Date).ToList();
                Close(times);
            }
            catch (Exception ex)
            {
                Vm.Error = ex.Message;
            }
            finally
            {
                Vm.IsBusy = false;
                Vm.Status = string.Empty;
            }
        }

        private static List<Pair> Scrape(string targetUrl)
        {
            if (!TrophyUtility.servingReady.WaitOne(TimeSpan.FromSeconds(30)))
            {
                throw new Exception("FlareSolverr is not running. Put flaresolverr next to the app or copy times manually.");
            }
            string safeUrl = (targetUrl ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
            string jsonPayload = "{\"cmd\": \"request.get\", \"url\": \"" + safeUrl + "\", \"maxTimeout\": 60000}";
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(75);
                StringContent content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                string response = client.PostAsync("http://localhost:8191/v1", content).GetAwaiter().GetResult()
                    .Content.ReadAsStringAsync().GetAwaiter().GetResult();
                using (JsonDocument json = JsonDocument.Parse(response))
                {
                    string html = json.RootElement.GetProperty("solution").GetProperty("response").GetString();
                    Regex regex = new Regex("<td class=\"date_earned\">\\s+<span class=\"sort\">\\d+</span>");
                    MatchCollection matches = regex.Matches(html ?? string.Empty);
                    List<Pair> pairs = new List<Pair>();
                    int i = 0;
                    foreach (Match match in matches)
                    {
                        pairs.Add(new Pair(i++, long.Parse(Regex.Match(match.Value, "\\d+").ToString())));
                    }
                    return pairs;
                }
            }
        }

        private static IEnumerable<long> SmartCopy(List<Pair> input, CopyFromDialogViewModel opt)
        {
            List<Pair> trophies = input.ToList();
            trophies.Sort((a, b) => a.Date.CompareTo(b.Date));
            Random rand = new Random();
            int min = (int)Math.Min(opt.MinMinutes, opt.MaxMinutes);
            int max = (int)Math.Max(opt.MinMinutes, opt.MaxMinutes);
            if (max <= min)
            {
                max = min + 1;
            }
            TimeSpan time = TimeSpan.FromDays(opt.Years * 365 + opt.Months * 30 + opt.Days)
                + TimeSpan.FromSeconds(rand.Next(min, max));
            long delta = Convert.ToInt64(time.TotalSeconds);
            for (int i = 0; i < trophies.Count - 1; ++i)
            {
                if (trophies[i].Date == 0)
                {
                    continue;
                }
                trophies[i].Date += delta;
                if (trophies[i + 1].Date - trophies[i].Date > 60)
                {
                    delta += rand.Next(min, max);
                }
            }
            if (trophies.Count > 0)
            {
                trophies[trophies.Count - 1].Date += delta;
            }
            trophies.Sort((a, b) => a.Id.CompareTo(b.Id));
            return trophies.Select(d => d.Date);
        }
    }
}
