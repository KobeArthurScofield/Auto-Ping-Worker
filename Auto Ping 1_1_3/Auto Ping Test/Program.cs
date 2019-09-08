using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.NetworkInformation;
using System.Threading;

namespace Auto_Ping_Test
{
    class Program
    {
        static void Main(string[] args)
        {
            Ping ping = new Ping();
            Console.WriteLine(NetworkInterface.GetIsNetworkAvailable());
            Console.WriteLine(NetworkInterface.GetAllNetworkInterfaces().Length);
            PingOptions pingOptions = new PingOptions();
            pingOptions.Ttl = 64;
            pingOptions.DontFragment = true;
            byte[] nulk = new byte[320];
            while (true)
            {
                PingReply pingreply = ping.Send("127.0.0.1");
                ping.Send("127.0.0.1", 4000, nulk, pingOptions);
                if (pingreply.Status == IPStatus.Success)
                {
                    Console.WriteLine("Destination OK");
                }
                Thread.Sleep(8000);
            }
        }
    }
}
