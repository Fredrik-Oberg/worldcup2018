using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
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
        private const string Isbusy = "IsBusy";
        private const string Lastupdatematch = "LastUpdateMatch";

        private int NumberOfParticipants { get; }
        private int Database { get; }
        private string GameId { get; }

        public Scraping()
        {
            NumberOfParticipants = 33;
            Database = 10;
            GameId = "55Pi";
        }
        
        private void WriteEvent(string msg)
        {
            try
            {
                Console.WriteLine(msg);
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

            var schedulesConcurrentMatches = 1;
            var scheduledMatchUtc = GetGameData(ref schedulesConcurrentMatches);

            WriteEvent($"{nameof(scheduledMatchUtc)}:{scheduledMatchUtc}" +
                       $"|{nameof(schedulesConcurrentMatches)}:{schedulesConcurrentMatches}");

            var savedLastMatchUpdate = redis.GetRedisValue<DateTime>(Lastupdatematch);

            var isBusy = redis.GetRedisValue<bool>(Isbusy);

            if (scheduledMatchUtc < DateTime.UtcNow && savedLastMatchUpdate != scheduledMatchUtc && isBusy == false)
            {
                redis.SetRedisValue(true, Isbusy);
                WriteEvent("Start scraping");
               
                try
                {
                    for (var i = 0; i < NumberOfParticipants; i += 3)
                    {
                        var tasks = new[]
                        {
                            
                            Task.Factory.StartNew(() =>
                                Scrape(NewWebDriver(),
                                    i, schedulesConcurrentMatches, scheduledMatchUtc, redis)),
                            Task.Factory.StartNew(() =>
                                Scrape(NewWebDriver(),
                                    i + 1, schedulesConcurrentMatches, scheduledMatchUtc, redis)),
                            Task.Factory.StartNew(() =>
                                Scrape(NewWebDriver(),
                                    i + 2, schedulesConcurrentMatches, scheduledMatchUtc, redis))
                        };

                        Task.WaitAll(tasks);
                    }

                    redis.SetRedisValue(scheduledMatchUtc, Lastupdatematch);
                }


                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    Console.WriteLine(e.StackTrace);
                }
                finally
                {
                    redis.SetRedisValue(false, Isbusy);
                }
            }
            else
            {
                WriteEvent($"No scraping needed currentTime: {DateTime.UtcNow} {Environment.NewLine}" +
                           $"|{nameof(scheduledMatchUtc)}:{scheduledMatchUtc} {Environment.NewLine}" +
                           $"|{nameof(savedLastMatchUpdate)}:{savedLastMatchUpdate} {Environment.NewLine}" +
                           $"|{nameof(isBusy)}:{isBusy} {Environment.NewLine}");
            }
        }

        private static RemoteWebDriver NewWebDriver()
        {
            var driverAddress = GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT").Equals("release", StringComparison.OrdinalIgnoreCase)
                ? "http://driver:4444/wd/hub"
                : "http://localhost:4444/wd/hub";

            return new RemoteWebDriver(new Uri(driverAddress), new ChromeOptions());
        }

        private static DateTime GetGameData(ref int schedulesConcurrentMatches)
        {
            using (StreamReader r = new StreamReader(AppDomain.CurrentDomain.BaseDirectory + "/assets/schedule.json"))
            {
                string json = r.ReadToEnd();
                var schedules = JsonConvert.DeserializeObject<IList<Schedule>>(json);
                foreach (var schedule in schedules)
                {
                    var parsedGameTime = DateTime.ParseExact(schedule.Start, "yyyy-MM-dd HH:mm",
                        CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

                    var scheduledMatchUtc = DateTime.SpecifyKind(parsedGameTime, DateTimeKind.Utc);

                    var hoursToAdd = 1.5;
                    Console.WriteLine($"scheduledMatchUtc + {hoursToAdd}:{scheduledMatchUtc.AddHours(hoursToAdd)}{Environment.NewLine} UTCTimeNow:{DateTime.UtcNow}");
                    
                    if (scheduledMatchUtc.AddHours(hoursToAdd) < DateTime.UtcNow) continue;

                    schedulesConcurrentMatches = schedule.ConcurrentGames;
                    return scheduledMatchUtc;
                }
            }

            return DateTime.MaxValue;
        }

        private void Scrape(IWebDriver driver,
            int index,
            int schedulesConcurrentMatches,
            DateTime scheduledMatchUtc,
            Redis redis)
        {
            var concurrentMatchesScraped = 0;
            try
            {
                if(index > NumberOfParticipants - 1) return;
                
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
                    ((IJavaScriptExecutor) driver).ExecuteScript("arguments[0].click();", participant);
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

                        WriteEvent($"{nameof(matchTimeutc)}:{matchTimeutc}");

                        if (scheduledMatchUtc != matchTimeutc)
                        {
                            continue;
                        }

                        match.MatchStart = matchTimeutc;
                        match.HomeTeam = GetTextFromFindElementByClass(row, "eventRowLeft");
                        match.AwayTeam = GetTextFromFindElementByClass(row, "eventRowRight");

                        WriteEvent($"{nameof(match.HomeTeam)}:{match.HomeTeam}" +
                                   " - " +
                                   $"|{nameof(match.AwayTeam)}:{match.AwayTeam}");

                        var events = TryAgainException(() => row.FindElement(By.ClassName("eventRowCenter")));
                        var fields = TryAgainException(() => events.FindElements(By.CssSelector("input")));

                        var result = fields[0].GetAttribute("value") + " - " + fields[1].GetAttribute("value");

                        match.Result = result;
                        WriteEvent($"{nameof(match.Result)}:{match.Result}");

                        RemoveOldMatch(user, match);

                        user.Matches.Add(match);

                        concurrentMatchesScraped++;

                        if (concurrentMatchesScraped == schedulesConcurrentMatches)
                        {
                            break;
                        }
                    }

                    WriteEvent("-------------------------------------------------");
                }

                user.LastUpdated = DateTime.Now.ToString("O");

                redis.SetRedisValue(user, Participants + user.Name);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);

                Console.WriteLine(e.StackTrace);
            }
            finally
            {
                driver.Quit();
            }
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

        public static T TryAgainException<T>(Func<T> action)
        {
            return TryAgain(action, 0, 3);
        }

        private static T TryAgain<T>(Func<T> action,
            int i,
            int limit)
        {
            try
            {
                return action();
            }
            catch (Exception e)
            {
                if (i >= limit)
                {
                    throw e;
                }

                var j = i + 1;
                return TryAgain(action, j, limit);
            }
        }
    }
}