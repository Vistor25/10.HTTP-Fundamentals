using SiteDownloader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleClient
{
    class Program
    {
        static void Main(string[] args)
        {
            Downloader downloader = new Downloader("https://www.onliner.by/", "D:/Site", 0);
            downloader.GetPages();
            Console.ReadKey();

        }
    }
}
