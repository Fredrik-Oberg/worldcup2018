using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using betscraping.Models;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Remote;
using OpenQA.Selenium.Support.UI;
using RedisLayer;
using static System.Environment;

namespace betscraping
{
    public class Scraping
    {
        private const string Participants = "participants:";
        private const string IsBusy = "IsBusy";
        private const string Lastupdatematch = "LastUpdateMatch";
        private const string LastFullUpdate = "LastFullUpdate";

        private int NumberOfParticipants { get; }
        private int Database { get; }
        private string GameId { get; }

        public Scraping()
        {
            NumberOfParticipants = 35;
            Database = 10;
            GameId = "55Pi";
        }

        private static void WriteEvent(string msg)
        {
            try
            {
                Console.WriteLine("- " + msg);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        public void ScrapeBetme()
        {
            WriteEvent("Check if time to scrape");

            var redis = new Redis(Database);

            var gameData = GetGameData();

            WriteEvent($"{nameof(gameData.MatchStartTime)}:{gameData.MatchStartTime}" +
                       $"|{nameof(gameData.ConcurrentGames)}:{gameData.ConcurrentGames}");

            var savedLastMatchUpdate = redis.GetRedisValue<DateTime>(Lastupdatematch);

            var isBusy = redis.GetRedisValue<bool>(IsBusy);
            var now = DateTime.UtcNow;

            if (gameData.MatchStartTime < DateTime.UtcNow && savedLastMatchUpdate != gameData.MatchStartTime && isBusy == false)
            {
                WriteEvent("Start scraping");

                Init(redis, gameData.ConcurrentGames, gameData.MatchStartTime);
                redis.SetRedisValue(false, IsBusy);

            }
            //Do a full scrape of every game played so far
            else if (isBusy == false && now.Hour == 4)
            {
                var lastFullUpdate = redis.GetRedisValue<DateTime>(LastFullUpdate);
                if (lastFullUpdate.Date >= now.Date) return;

                WriteEvent("Start full scraping");

                var gamesUntilNow = GetGameDataForMatchesUntilNow();
                foreach (var game in gamesUntilNow)
                {
                    Init(redis, game.ConcurrentGames, game.MatchStartTime);
                }

                redis.SetRedisValue(now, LastFullUpdate);

                redis.SetRedisValue(false, IsBusy);
            }
            else
            {
                WriteEvent($"No scraping needed currentTime: {DateTime.UtcNow} {NewLine}" +
                           $"|{nameof(savedLastMatchUpdate)}:{savedLastMatchUpdate} {NewLine}" +
                           $"|{nameof(isBusy)}:{isBusy} {NewLine}");
            }
        }

        private void Init(Redis redis,
            int schedulesConcurrentMatches,
            DateTime scheduledMatchUtc)
        {
            var driver1 = NewWebDriver();
            var driver2 = NewWebDriver();
            var driver3 = NewWebDriver();

            try
            {
                redis.SetRedisValue(true, IsBusy);

                for (var i = 0; i < NumberOfParticipants; i += 3)
                {
                    var tasks = new[]
                    {
                        Task.Factory.StartNew(() =>
                            TryScrape(driver1,
                                i, schedulesConcurrentMatches, scheduledMatchUtc, redis)),
                        Task.Factory.StartNew(() =>
                            TryScrape(driver2,
                                i + 1, schedulesConcurrentMatches, scheduledMatchUtc, redis)),
                        Task.Factory.StartNew(() =>
                            TryScrape(driver3,
                                i + 2, schedulesConcurrentMatches, scheduledMatchUtc, redis))
                    };

                    Task.WaitAll(tasks);
                }

                redis.SetRedisValue(scheduledMatchUtc, Lastupdatematch);
            }

            catch (Exception e)
            {
                WriteEvent($"Scraping:{NewLine}{e.Message}{NewLine}{e.StackTrace}");
            }
            finally
            {
                driver1.Quit();
                driver2.Quit();
                driver3.Quit();

            }
        }

        private void TryScrape(IWebDriver driver,
            int index,
            int schedulesConcurrentMatches,
            DateTime scheduledMatchUtc,
            Redis redis)
        {
            if (index > NumberOfParticipants - 1) return;

            TryAgainException(() => Scrape(driver, index, schedulesConcurrentMatches, scheduledMatchUtc, redis), 10);
        }

        private bool Scrape(IWebDriver driver,
            int index,
            int schedulesConcurrentMatches,
            DateTime scheduledMatchUtc,
            Redis redis)
        {
            var concurrentMatchesScraped = 0;

            driver.Navigate().GoToUrl($"http://www.betme.se/game.html?id={GameId}");
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));

            var usersTabSelect = wait.Until(ExpectedConditions.ElementIsVisible(By.Id("usersTabSelect")));
            var userForm = TryAgainException(() =>
            {
                usersTabSelect.Click();
                return wait.Until(ExpectedConditions.ElementIsVisible(By.Id("usersForm")));
            });
            var participantsRow = userForm.FindElements(By.CssSelector("tr"));

            //Get participants row for current index
            var partRow = participantsRow[index];

            var participant = partRow.FindElement(By.CssSelector("a"));

            var participantScore = partRow.FindElements(By.CssSelector("td"))[2];

            //Get the score
            var points = participantScore.Text.Replace("Points", "");
            var participantText = $"{participant.Text} {points}";

            WriteEvent(participantText);

            var user = redis.GetRedisValue<Participant>(Participants + participant.Text)
                       ?? new Participant(participant.Text);

            user.Points = points;

            if (participant.Displayed)
            {
                participant.Click();
            }
            else
            {
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", participant);
            }

            ReadOnlyCollection<IWebElement> boxes = null;
            boxes = TryAgainException(() =>
            {
                IWebElement userTabToRender = null;
                userTabToRender = wait.Until(ExpectedConditions.ElementIsVisible(By.Id("userTabToRender")));
                return userTabToRender.FindElements(By.ClassName("boxHalfInTab"));
            });

            foreach (var box in boxes)
            {
                if (concurrentMatchesScraped == schedulesConcurrentMatches)
                {
                    break;
                }

                IWebElement table = null;
                table = TryAgainException(() => box.FindElement(By.ClassName("eventRow")));

                ReadOnlyCollection<IWebElement> rows = null;
                //Get all rows except for time row, that is in .rowsepartor
                rows = TryAgainException(() => table.FindElements(By.CssSelector("tr:not(.rowSeparator)")));

                var rowIndex = 0;
                ReadOnlyCollection<IWebElement> rowSeparators = null;

                //in this container the match start timer is found
                rowSeparators = TryAgainException(() => table.FindElements(By.CssSelector("tr.rowSeparator")));

                foreach (var row in rows)
                {
                    var match = new Match();

                    var matchTimeutc = GetMatchTimeutc(rowSeparators, rowIndex++);

                    if (scheduledMatchUtc != matchTimeutc)
                    {
                        continue;
                    }

                    WriteEvent($"{user.Name}: {nameof(matchTimeutc)}: {matchTimeutc}");

                    match.MatchStart = matchTimeutc;
                    match.HomeTeam = GetTextFromFindElementByClass(row, "eventRowLeft");
                    match.AwayTeam = GetTextFromFindElementByClass(row, "eventRowRight");

                    WriteEvent($"{user.Name}: {nameof(match.HomeTeam)}:{match.HomeTeam} - {nameof(match.AwayTeam)}:{match.AwayTeam}");

                    var events = TryAgainException(() => row.FindElement(By.ClassName("eventRowCenter")));
                    var fields = TryAgainException(() => events.FindElements(By.CssSelector("input")));

                    var result = fields[0].GetAttribute("value") + " - " + fields[1].GetAttribute("value");

                    match.Result = result;
                    WriteEvent($"{user.Name}: {nameof(match.Result)}: {match.Result}");

                    RemoveOldMatch(user, match);

                    user.Matches.Add(match);

                    concurrentMatchesScraped++;

                    if (concurrentMatchesScraped == schedulesConcurrentMatches)
                    {
                        break;
                    }
                }
            }

            user.LastUpdated = DateTime.Now.ToString("O");

            redis.SetRedisValue(user, Participants + user.Name);
            return true;
        }
        private static RemoteWebDriver NewWebDriver()
        {
            var driverAddress = GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT").Equals("release", StringComparison.OrdinalIgnoreCase)
                ? "http://driver:4444/wd/hub"
                : "http://localhost:4444/wd/hub";

            return new RemoteWebDriver(new Uri(driverAddress), new ChromeOptions());
        }

        private static GameData GetGameData()
        {
            using (StreamReader r = new StreamReader(AppDomain.CurrentDomain.BaseDirectory + "/assets/schedule.json"))
            {
                var json = r.ReadToEnd();
                var schedules = JsonConvert.DeserializeObject<IList<Schedule>>(json);
                foreach (var schedule in schedules)
                {
                    var parsedGameTime = DateTime.ParseExact(schedule.Start, "yyyy-MM-dd HH:mm",
                        CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

                    var scheduledMatchUtc = DateTime.SpecifyKind(parsedGameTime, DateTimeKind.Utc);

                    var hoursToAdd = 1.5;

                    if (scheduledMatchUtc.AddHours(hoursToAdd) < DateTime.UtcNow) continue;

                    WriteEvent($"scheduledMatchUtc + {hoursToAdd}:{scheduledMatchUtc.AddHours(hoursToAdd)}{NewLine} UTCTimeNow:{DateTime.UtcNow}");

                    return new GameData
                    {
                        MatchStartTime = scheduledMatchUtc,
                        ConcurrentGames = schedule.ConcurrentGames
                    };
                }
            }

            return new GameData
            {
                ConcurrentGames = 0,
                MatchStartTime = DateTime.MaxValue
            };
        }
        private List<GameData> GetGameDataForMatchesUntilNow()
        {
            var matchesUntilNow = new List<GameData>();
            using (StreamReader r = new StreamReader(AppDomain.CurrentDomain.BaseDirectory + "/assets/schedule.json"))
            {
                var json = r.ReadToEnd();
                var schedules = JsonConvert.DeserializeObject<IList<Schedule>>(json);
                foreach (var schedule in schedules)
                {
                    var parsedGameTime = DateTime.ParseExact(schedule.Start, "yyyy-MM-dd HH:mm",
                        CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

                    var scheduledMatchUtc = DateTime.SpecifyKind(parsedGameTime, DateTimeKind.Utc);
                    var now = DateTime.UtcNow;

                    if (scheduledMatchUtc > now) break;

                    WriteEvent($"GetGameDataForMatchesUntilNow {NewLine}scheduledMatchUtc: {scheduledMatchUtc}{NewLine} UTCTimeNow:{now}");

                    matchesUntilNow.Add(new GameData
                    {
                        MatchStartTime = scheduledMatchUtc,
                        ConcurrentGames = schedule.ConcurrentGames,

                    });
                }
            }

            return matchesUntilNow;
        }


        private static void RemoveOldMatch(Participant user,
            Match match)
        {
            var matchAlreadySaved = user.Matches.FirstOrDefault(x => x.MatchStart == match.MatchStart);
            if (matchAlreadySaved != null)
            {
                user.Matches.Remove(matchAlreadySaved);
            }
        }

        private static DateTime GetMatchTimeutc(ReadOnlyCollection<IWebElement> rowSeparators,
            int rowIndex)
        {
            var matchTime = rowSeparators[rowIndex].FindElement(By.CssSelector("span")).Text;

            var parsedMatchTime = DateTime.Parse(matchTime, CultureInfo.InvariantCulture, DateTimeStyles.None);

            var matchTimeutc = SetTimeZoneForDate(parsedMatchTime, "UTC");
            return matchTimeutc;
        }

        private static string GetTextFromFindElementByClass(IWebElement row,
            string @class)
        {
            return TryAgainException(() => row.FindElement(By.ClassName(@class))).Text;
        }

        private static DateTime SetTimeZoneForDate(DateTime unset,
            string timezone)
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return TimeZoneInfo.ConvertTimeToUtc(unset, tz);
        }

        private static T TryAgainException<T>(Func<T> action)
        {
            return TryAgain(action, 0, 3);
        }
        private static T TryAgainException<T>(Func<T> action, int sleep)
        {
            return TryAgain(action, 0, 3, sleep);
        }

        private static T TryAgain<T>(Func<T> action,
            int i,
            int limit,
            int sleep = 0)
        {
            try
            {
                return action();
            }
            catch (Exception e)
            {
                WriteEvent($"TryAgainException: i:{i} {NewLine}{e.Message}{NewLine}{e.StackTrace}");

                if (i >= limit)
                {
                    throw e;
                }

                var j = i + 1;
                if (sleep > 0)
                {
                    Thread.Sleep(new TimeSpan(0, 0, 0, sleep));
                }
                return TryAgain(action, j, limit);
            }
        }
    }

    internal class GameData
    {
        public DateTime MatchStartTime { get; set; }
        public int ConcurrentGames { get; set; }
    }
}