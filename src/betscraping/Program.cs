using System;
using System.Collections.Generic;
using System.Threading;

namespace betscraping
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Started app");
            while (true)
            {
                new Scraping().ScrapeBetme();
                var sleepTime = 120000;
                Console.WriteLine("Sleep for: " + sleepTime);
                Thread.Sleep(sleepTime);
            }

        }
    }
}