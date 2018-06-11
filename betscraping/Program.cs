using System;
using System.Collections.Generic;

namespace betscraping
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Started");
            new Scraping().ScrapeBetme();
        }
    }
}