using System.Net.NetworkInformation;

namespace AutoPingTest_Net;

internal static class Program
{
    private static void Main(string[] args)
    {
        var ping = new Ping();
        Console.WriteLine($"Network available: {NetworkInterface.GetIsNetworkAvailable()}");
        Console.WriteLine($"Network interfaces: {NetworkInterface.GetAllNetworkInterfaces().Length}");

        var pingOptions = new PingOptions
        {
            Ttl = 64,
            DontFragment = true
        };

        var payload = new byte[320];
        while (true)
        {
            var pingReply = ping.Send("127.0.0.1");
            ping.Send("127.0.0.1", 4000, payload, pingOptions);

            if (pingReply.Status == IPStatus.Success)
            {
                Console.WriteLine("Destination OK");
            }

            Thread.Sleep(8000);
        }
    }
}
