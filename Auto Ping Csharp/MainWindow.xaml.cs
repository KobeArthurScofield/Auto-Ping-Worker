using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Net.NetworkInformation;
using System.Threading;
using System.Collections;

namespace Auto_Ping_Csharp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public class DataSct
    {
        public struct PingParam
        {
            public string destination;
            public Int32 buffersize;
            public bool dflag;
            public Int32 ttl;
            public Int32 timeout;
            public Int32 interval;
        };
    }

    public partial class MainWindow : Window
    {
        public delegate void StatusUpdater(Int32 updatefield, string data);
        public delegate void FieldUpdater(Int32 updatefield, string data);
        public Thread localcheck, networkstate, pingworker;
        public static Int32 default_buffer = 32, default_ttl = 64, default_timeout = 5000, default_interval = 1000, default_timewindow = 120000;
        public ArrayList RTT = new ArrayList();
        public Int32 statisticpackcount, sentpackcount, successpackcount, failedpackcount;
        public Int64 totalrtt;
        public EventWaitHandle alwayson = new EventWaitHandle(false, EventResetMode.ManualReset);
        public EventWaitHandle controlon = new EventWaitHandle(false, EventResetMode.ManualReset);
        public EventWaitHandle sign = new EventWaitHandle(false, EventResetMode.AutoReset);
        

        public MainWindow()
        {
            InitializeComponent();
            Stop_Button.IsEnabled = false;
            localcheck = new Thread(new ThreadStart(LocalTestLauncher))
            {
                IsBackground = true,
                Name = "Background Loopback Check",
                Priority = ThreadPriority.BelowNormal
            };
            networkstate = new Thread(new ThreadStart(NetworkStateLauncher))
            {
                IsBackground = true,
                Name = "Background Network Availability Check",
                Priority = ThreadPriority.Lowest
            };
            alwayson.Reset();
            localcheck.Start();
            networkstate.Start();
        }

        public void LocalTestLauncher()
        {
            Timer timer = new Timer(LocalTest, new AutoResetEvent(false), 0, 4000);
            alwayson.WaitOne();
            timer.Dispose(alwayson);
        }

        public void LocalTest(object sender)
        {
            Ping loopbackping = new Ping();
            bool loopv4, loopv6;
            StatusUpdater update = StdUpd;
            PingReply rplv4 = loopbackping.Send("127.0.0.1", 500), rplv6 = loopbackping.Send("::1", 500);
            if (rplv4.Status == IPStatus.Success)
                loopv4 = true;
            else
                loopv4 = false;
            if (rplv6.Status == IPStatus.Success)
                loopv6 = true;
            else
                loopv6 = false;
            if (loopv4 && loopv6)
                Dispatcher.Invoke(update, 2, "V4/V6");
            else if(loopv4)
                Dispatcher.Invoke(update, 2, "V4");
            else if(loopv6)
                Dispatcher.Invoke(update, 2, "V6");
            else
                Dispatcher.Invoke(update, 2, "Failed");
        }

        public void PingerLauncher(object pingparam)
        {
            DataSct.PingParam pingparamdata = (DataSct.PingParam)pingparam;
            statisticpackcount = default_timewindow / pingparamdata.interval;
            sentpackcount = 0;
            successpackcount = 0;
            failedpackcount = 0;
            totalrtt = 0;
            RTT.Clear();
            Timer timer = new Timer(Pinger, pingparam, 0, pingparamdata.interval);
            controlon.WaitOne();
            timer.Dispose(controlon);
            sign.Set();
        }

        public void Pinger(object pingparam)
        {
            DataSct.PingParam pingparamdata = (DataSct.PingParam)pingparam;
            Ping pingwork = new Ping();
            StatusUpdater statusUpdater = StdUpd;
            try
            {
                PingReply pingReply = pingwork.Send(pingparamdata.destination, pingparamdata.timeout, new byte[pingparamdata.buffersize], new PingOptions(pingparamdata.ttl, pingparamdata.dflag));
                if (sentpackcount < statisticpackcount)
                    sentpackcount += 1;
                if (pingReply.Status == IPStatus.Success)
                {
                    PackCouter(true, pingReply.RoundtripTime);
                    Dispatcher.Invoke(statusUpdater, 5, "OK");
                }
                else
                {
                    PackCouter(false, 0);
                    Dispatcher.Invoke(statusUpdater, -1, DateTime.UtcNow.ToString() + " " + pingparamdata.destination + " " + ICMPErrorAnalasys(pingReply.Status));
                    Dispatcher.Invoke(statusUpdater, 5, "Failed");
                }
            }
            catch (Exception exception)
            {
                ExceptionLogcat(exception);
                controlon.Set();
            }
            Int32 averagepingtime, packetlossrate;
            averagepingtime = (Int32)(Math.Ceiling(((double)totalrtt) / ((double)successpackcount)));
            packetlossrate = (Int32)(Math.Ceiling((double)failedpackcount) / ((double)sentpackcount) * 100.0);
            Dispatcher.Invoke(statusUpdater, 3, averagepingtime.ToString());
            Dispatcher.Invoke(statusUpdater, 4, packetlossrate.ToString());
        }

        public void PackCouter(bool issuccess, Int64 roundtriptime)
        {
            if (!(RTT.Count < statisticpackcount))
            {
                if ((Int64)RTT[0] != 0)
                    successpackcount -= 1;
                else
                    failedpackcount -= 1;
                totalrtt -= (Int64)RTT[0];
                RTT.RemoveAt(0);
            }
            if (issuccess)
                successpackcount += 1;
            else
                failedpackcount += 1;
            RTT.Add(roundtriptime);
            totalrtt += roundtriptime;
        }

        public void NetworkStateLauncher()
        {
            Timer timer = new Timer(NetworkState, new AutoResetEvent(false), 0, 4000);
            alwayson.WaitOne();
            timer.Dispose(alwayson);
        }

        public void NetworkState(object sender)
        {
            StatusUpdater statusUpdater = StdUpd;
            if (NetworkInterface.GetIsNetworkAvailable())
                Dispatcher.Invoke(statusUpdater, 1, "Available");
            else
                Dispatcher.Invoke(statusUpdater, 1, "Not Available");
        }

        public void StdUpd(Int32 field, string data)
        {
            switch(field)
            {
                case 1: NWStatus.Content = "Network Status: " + data; break;
                case 2: LCPing.Content = "Loopback: " + data; break;
                case 3: Average_Ping.Content = "Ping: " + data + "ms"; break;
                case 4: Pack_Loss.Content = "PL:" + data + "%"; break;
                case 5: Ping_Status.Content = "Current Ping: " + data; break;
                case -1: Error_Status.Text += (data + '\n'); break;
                default:break;
            }
        }

        public void FldUpd(Int32 field, string data)
        {
            switch(field)
            {
                case 1: Destination_Fill.Text = data; break;
                case 2: Buffer_Size.Text = data; break;
                case 3: TTL_Count.Text = data; break;
                case 4: Timeout_Count.Text = data; break;
                case 5: Interval_Count.Text = data; break;
                default: StdUpd(-1, "WTH"); break;
            }
        }

        public bool CheckANumber(string input)
        {
            Int32 a = input.Length;
            bool flag = true;
            for (Int32 i = 0; i < a; i++)
                if (((byte)input[i] < 48) || ((byte)input[i] > 57))
                    flag = false;
            return flag;
        }

        public Int32 CheckNumberBetween(string input, Int32 min, Int32 max)
        {
            Int32 result;
            result = Convert.ToInt32(input);
            if (result >= min && result <= max)
                return result;
            else
                return -1;
        }

        public Int32 CheckNumberLarger(string input,Int32 floor)
        {
            Int32 result;
            result = Convert.ToInt32(input);
            if (result >= floor)
                return result;
            else
                return -1;
        }

        public string ICMPErrorAnalasys(IPStatus iPStatus)
        {
            switch (iPStatus)
            {
                case IPStatus.Success: return "Ping OK";
                case IPStatus.BadDestination: return "Destination cannot receive echo or this is not a proper address";
                case IPStatus.BadHeader: return "The header is invalid";
                case IPStatus.BadOption: return "The ping option is invalid";
                case IPStatus.BadRoute: return "No valid route between you and the destination";
                case IPStatus.DestinationHostUnreachable: return "Destination unreachable";
                case IPStatus.DestinationNetworkUnreachable: return "Destination with its network unreachable";
                case IPStatus.DestinationPortUnreachable: return "Destination port unreachable";
                /*case IPStatus.DestinationProhibited: return "Destination prohibited";*/
                /*case IPStatus.DestinationProtocolUnreachable: return "Destination protocol unreachale";*/
                case (IPStatus)11004: return "Destination protocol unreachable (IPv4) or Destination prohibited (IPv6) for configure reason";
                case IPStatus.DestinationScopeMismatch:return "Destination Scope Mismatch";
                case IPStatus.DestinationUnreachable: return "Destination unreachablefor unknown reason";
                case IPStatus.HardwareError: return "Hardware error";
                case IPStatus.IcmpError: return "ICMP error";
                case IPStatus.NoResources: return " No sufficient network resources";
                case IPStatus.PacketTooBig: return "Package too big";
                case IPStatus.ParameterProblem: return "Somewhere cannot read the header properly";
                case IPStatus.SourceQuench: return " Packet discarded because of you have not enough network queue or the destination failed to process";
                case IPStatus.TimedOut: return "Timed out";
                case IPStatus.TimeExceeded: return "TTL zeroed";
                case IPStatus.TtlExpired: return "TTL expired";
                case IPStatus.TtlReassemblyTimeExceeded: return "Some of fragments lost";
                case IPStatus.UnrecognizedNextHeader: return "Not a readable TCP or UDP indicator";
                case IPStatus.Unknown: return "Unknoun reason";
                default: return "Unknown callback";
            }
        }

        public void ExceptionLogcat(Exception exception)
        {
            StatusUpdater statusUpdater = StdUpd;
            Dispatcher.Invoke(statusUpdater, -1, DateTime.Now.ToString());
            if (exception.Message != null)
                Dispatcher.Invoke(statusUpdater, -1, "==MESSAGE==\n" + exception.Message);
            if (exception.InnerException != null)
                Dispatcher.Invoke(statusUpdater, -1, "==INNER EXCEPTION==\n" + exception.InnerException.ToString());
            if (exception.Source != null)
                Dispatcher.Invoke(statusUpdater, -1, "==Source==\n" + exception.Source);
            if (exception.TargetSite != null)
                Dispatcher.Invoke(statusUpdater, -1, "==TARGET SIZE==\n" + exception.TargetSite.ToString());
            if (exception.Data != null)
                Dispatcher.Invoke(statusUpdater, -1, "==DATA==\n" + exception.Data.ToString());
            if (exception.StackTrace != null)
                Dispatcher.Invoke(statusUpdater, -1, "==STACK TRACE==\n" + exception.StackTrace);
            if (exception.HelpLink != null)
                Dispatcher.Invoke(statusUpdater, -1, "==HELP LINK==\n" + exception.HelpLink);
        }

        private void Start_Button_Click(object sender, RoutedEventArgs e)
        {
            string dest = "";
            Int32 bufferlength = default_buffer;
            Int32 ttlvalue = default_ttl;
            Int32 timeout = default_timeout;
            Int32 interval = default_interval;
            byte checker = 0x00;    //A bitfield checker
            FieldUpdater fieldUpdater = FldUpd;
            StatusUpdater statusUpdater = StdUpd;
            //Valid IP or domain name
            if (Destination_Fill.Text != "")
            {
                dest = Destination_Fill.Text;
                checker = (byte)(checker | (byte)0x01);
            }
            else
                Dispatcher.Invoke(statusUpdater, -1, "No destination filled.");
            //Valid buffer size
            if (Buffer_Size.Text != "")
                if (CheckANumber(Buffer_Size.Text))
                    if ((bufferlength = CheckNumberBetween(Buffer_Size.Text, 32, 65500)) != -1)
                        checker = (byte)(checker | (byte)0x02);
                    else
                        Dispatcher.Invoke(statusUpdater, -1, "Invalid buffer size setting.");
                else
                    Dispatcher.Invoke(statusUpdater, -1, "Invalid buffer size Input.");
            else
            {
                Dispatcher.Invoke(fieldUpdater, 2, bufferlength.ToString());
                checker = (byte)(checker | (byte)0x02);
            }
            //Valid TTL
            if (TTL_Count.Text != "")
                if (CheckANumber(TTL_Count.Text))
                    if ((ttlvalue = CheckNumberBetween(TTL_Count.Text, 1, 255)) != -1)
                        checker = (byte)(checker | (byte)0x04);
                    else
                        Dispatcher.Invoke(statusUpdater, -1, "Invalid TTL Value.");
                else
                    Dispatcher.Invoke(statusUpdater, -1, "Invalid TTL Input.");
            else
            {
                Dispatcher.Invoke(fieldUpdater, 3, ttlvalue.ToString());
                checker = (byte)(checker | (byte)0x04);
            }
            //Valid Timeout
            if (Timeout_Count.Text != "")
                if (CheckANumber(Timeout_Count.Text))
                    if ((timeout = CheckNumberLarger(Timeout_Count.Text, 1)) != -1)
                        checker = (byte)(checker | (byte)0x08);
                    else { }
                else
                    Dispatcher.Invoke(statusUpdater, -1, "Invalid timeout input.");
            else
            {
                Dispatcher.Invoke(fieldUpdater, 4, timeout.ToString());
                checker = (byte)(checker | (byte)0x08);
            }
            //Valid interval
            if (Interval_Count.Text != "")
                if (CheckANumber(Interval_Count.Text))
                    if ((interval = CheckNumberLarger(Interval_Count.Text, 1)) != -1)
                        checker = (byte)(checker | (byte)0x10);
                    else { }
                else
                    Dispatcher.Invoke(statusUpdater, -1, "Invalid interval input.");
            else
            {
                Dispatcher.Invoke(fieldUpdater, 5, interval.ToString());
                checker = (byte)(checker | (byte)0x10);
            }
            //Check Valid
            if (checker == 0x1F)
            {
                DataSct.PingParam pingparamdata = new DataSct.PingParam
                {
                    destination = dest,
                    buffersize = bufferlength,
                    dflag = Is_DF.IsChecked.Value,
                    ttl = ttlvalue,
                    timeout = timeout,
                    interval = interval
                };
                Stop_Button.IsEnabled = true;
                Start_Button.IsEnabled = false;
                Error_Status.Text = null;
                pingworker = new Thread(new ParameterizedThreadStart(PingerLauncher))
                {
                    IsBackground = true,
                    Name = "Ping Worker",
                    Priority = ThreadPriority.AboveNormal
                };
                controlon.Reset();
                pingworker.Start((object)pingparamdata);
            }
        }

        private void Stop_Button_Click(object sender, RoutedEventArgs e)
        {
            controlon.Set();
            sign.WaitOne();
            Error_Status.Text += "\n";
            Start_Button.IsEnabled = true;
            Stop_Button.IsEnabled = false;
        }

        private void MainWindowClosed(object sender, EventArgs e)
        {
            controlon.Set();
            alwayson.Set();
        }
    }
}
