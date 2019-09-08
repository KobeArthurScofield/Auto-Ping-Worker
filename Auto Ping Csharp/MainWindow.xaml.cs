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
            public string   destination;
            public Int32    buffersize;
            public bool     dflag;
            public Int32    ttl;
            public Int32    timeout;
            public Int32    interval;
        };
    }

    public class ValueSign
    {
        public enum StatusSign
        {
            NetworkAvailability =   1,
            Loopback            =   2,
            SmoothPing          =   3,
            PackageLoss         =   4,
            CurrentPing         =   5,
            Exception           =  -1,
            Error               =  -2,
            Warning             =  -3,
            Important           =  -4,
            Information         =  -5
        };

        public enum FieldSign
        {
            Destination = 1,
            BufferSize  = 2,
            TTL         = 3,
            TimeOut     = 4,
            Interval    = 5
        };
    }

    public partial class MainWindow : Window
    {
        public delegate void StatusUpdater(ValueSign.StatusSign updatefield, string data);
        public delegate void FieldUpdater(ValueSign.FieldSign updatefield, string data);
        public Thread localcheck, networkstate, pingworker;
        public static Int32 default_buffer = 32, default_ttl = 64, default_timeout = 5000, default_interval = 1000, default_timewindow = 120000,
            default_networkcheckinterval = 4000, default_loopbackcheckinterval = 4000;
        public ArrayList RTT = new ArrayList();
        public Int32 statisticpackcount, sentpackcount, successpackcount, failedpackcount;
        public Int64 totalrtt;
        public EventWaitHandle alwayson = new EventWaitHandle(false, EventResetMode.ManualReset);
        public EventWaitHandle controlon = new EventWaitHandle(false, EventResetMode.ManualReset);
        public object totalaccesslock = new object();
        public object statisticaccesslock = new object();
        public object statusrefreshlock = new object();

        public MainWindow()
        {
            InitializeComponent();
            StatusUpdater statusUpdater = StdUpd;
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Information, "Program initialized");
            UIElementEnabler(true);
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
            networkstate.Start();
            localcheck.Start();
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Important, "Launcher called.");
        }

        public void LocalTestLauncher()
        {
            Int32 loopbackcheckinterval = default_loopbackcheckinterval;
            StatusUpdater statusUpdater = StdUpd;
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Important, "Loopback check launcher started, check interval: " + loopbackcheckinterval.ToString("0ms"));
            Timer timer = new Timer(LocalTest, new AutoResetEvent(false), 0, loopbackcheckinterval);
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Information, "Loopback check lighter started.");
            alwayson.WaitOne();
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Warning, "Loopback check launcher dying.");
            timer.Dispose(alwayson);
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Warning, "Loopback check lighter broken.");
        }

        public void LocalTest(object sender)
        {
            Ping loopbackping = new Ping();
            bool loopv4, loopv6;
            StatusUpdater statusUpdate = StdUpd;
            PingReply rplv4 = loopbackping.Send("127.0.0.1", 100), rplv6 = loopbackping.Send("::1", 100);
            if (rplv4.Status == IPStatus.Success)
                loopv4 = true;
            else
                loopv4 = false;
            if (rplv6.Status == IPStatus.Success)
                loopv6 = true;
            else
                loopv6 = false;
            if (localcheck.IsAlive)
            {
                if (loopv4 && loopv6)
                    Dispatcher.Invoke(statusUpdate, ValueSign.StatusSign.Loopback, "V4/V6");
                else if (loopv4)
                    Dispatcher.Invoke(statusUpdate, ValueSign.StatusSign.Loopback, "V4");
                else if (loopv6)
                    Dispatcher.Invoke(statusUpdate, ValueSign.StatusSign.Loopback, "V6");
                else
                    Dispatcher.Invoke(statusUpdate, ValueSign.StatusSign.Loopback, "Failed");
            }
            else
            {
                Dispatcher.Invoke(statusUpdate, ValueSign.StatusSign.Loopback, "Unknown");
                Dispatcher.Invoke(statusUpdate, ValueSign.StatusSign.Error, "Loopback check launcher died.");
            }
        }

        public void PingerLauncher(object pingparam)
        {
            StatusUpdater statusUpdater = StdUpd;
            DataSct.PingParam pingparamdata = (DataSct.PingParam)pingparam;
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Important, "Ping worker launcher started.");
            lock (totalaccesslock)
            {
                statisticpackcount = default_timewindow / pingparamdata.interval;
                sentpackcount = 0;
            }
            lock (statisticaccesslock)
            {
                successpackcount = 0;
                failedpackcount = 0;
                totalrtt = 0;
                RTT.Clear();
            }
            Timer timer = new Timer(Pinger, pingparam, 0, pingparamdata.interval);
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Information, "Ping worker lighter started.");
            controlon.WaitOne();
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Warning, "Ping worker launcher dying.");
            timer.Dispose(controlon);
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Warning, "Ping worker lighter broken.");
        }

        public void Pinger(object pingparam)
        {
            DataSct.PingParam pingparamdata = (DataSct.PingParam)pingparam;
            Ping pingwork = new Ping();
            StatusUpdater statusUpdater = StdUpd;
            try
            {
                PingReply pingReply = pingwork.Send(pingparamdata.destination, pingparamdata.timeout, new byte[pingparamdata.buffersize], new PingOptions(pingparamdata.ttl, pingparamdata.dflag));
                lock (totalaccesslock)
                {
                    if (sentpackcount < statisticpackcount)
                        sentpackcount += 1;
                }
                if (pingReply.Status == IPStatus.Success)
                {
                    PackCouter(true, pingReply.RoundtripTime);
                    Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.CurrentPing, pingReply.RoundtripTime.ToString("0ms"));
                    if (((pingReply.RoundtripTime > pingparamdata.interval) && (pingparamdata.interval > 500)) || (pingReply.RoundtripTime >= 1000))
                        Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Warning, pingparamdata.destination + " ICMP reply latecy too long: " + pingReply.RoundtripTime.ToString("0ms"));
                }
                else
                {
                    PackCouter(false, 0);
                    Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Error, pingparamdata.destination + " " + ICMPErrorAnalasys(pingReply.Status));
                    Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.CurrentPing, "Failed");
                }
            }
            catch (Exception exception)
            {
                ExceptionLogcat(exception);
                controlon.Set();
            }
            double averagepingtime, packetlossrate;
            lock (statisticaccesslock)
            {
                averagepingtime = (double)totalrtt / (double)successpackcount;
                lock (totalaccesslock)
                {
                    packetlossrate = (double)failedpackcount / (double)sentpackcount;
                }
            }
            if (averagepingtime >= 0)
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.SmoothPing, averagepingtime.ToString("0.00ms"));
            else
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.SmoothPing, "-");
            if (packetlossrate >= 0)
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.PackageLoss, packetlossrate.ToString("0.00%"));
            else
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.PackageLoss, "-");
            if (!pingworker.IsAlive)
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Error, "Ping worker launcher died.");
        }

        public void PackCouter(bool issuccess, Int64 roundtriptime)
        {
            lock (statisticaccesslock)
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
        }

        public void NetworkStateLauncher()
        {
            Int32 networkcheckinterval = default_networkcheckinterval;
            StatusUpdater statusUpdater = StdUpd;
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Important, "Network availability check launcher started, interval: " + networkcheckinterval.ToString("0ms"));
            Timer timer = new Timer(NetworkState, new AutoResetEvent(false), 0, networkcheckinterval);
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Information, "Network availability check lighter started.");
            alwayson.WaitOne();
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Warning, "Network availability check launcher dying.");
            timer.Dispose(alwayson);
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Warning, "Network availability check lighter broken.");
        }

        public void NetworkState(object sender)
        {
            StatusUpdater statusUpdater = StdUpd;
            if (networkstate.IsAlive)
            {
                if (NetworkInterface.GetIsNetworkAvailable())
                    Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.NetworkAvailability, "Available");
                else
                    Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.NetworkAvailability, "Not Available");
            }
            else
            {
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.NetworkAvailability, "Unknown");
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Error, "Network availability check launcher died.");
            }
        }

        public void StdUpd(ValueSign.StatusSign field, string data)
        {
            DateTime dateTimeUTC = DateTime.UtcNow;
            string timeUTCstring = dateTimeUTC.Hour.ToString("00:") + dateTimeUTC.Minute.ToString("00:") + dateTimeUTC.Second.ToString("00") + "." + dateTimeUTC.Millisecond.ToString("000");
            lock (statusrefreshlock)
            {
                switch (field)
                {
                    case ValueSign.StatusSign.NetworkAvailability: NWStatus.Content = "Network:" + data; break;
                    case ValueSign.StatusSign.Loopback: LCPing.Content = "Loopback:" + data; break;
                    case ValueSign.StatusSign.SmoothPing: Average_Ping.Content = "SmoothPing:" + data; break;
                    case ValueSign.StatusSign.PackageLoss: Pack_Loss.Content = "PL:" + data; break;
                    case ValueSign.StatusSign.CurrentPing: Ping_Status.Content = "CurrentPing:" + data; break;
                    case ValueSign.StatusSign.Exception: Logcat_Display.Text += (timeUTCstring + " XX" + data + "\n"); break;
                    case ValueSign.StatusSign.Error: Logcat_Display.Text += (timeUTCstring + " X " + data + "\n"); break;
                    case ValueSign.StatusSign.Warning: Logcat_Display.Text += (timeUTCstring + " ! " + data + "\n"); break;
                    case ValueSign.StatusSign.Important: Logcat_Display.Text += (timeUTCstring + " o " + data + "\n"); break;
                    case ValueSign.StatusSign.Information: Logcat_Display.Text += (timeUTCstring + " i " + data + "\n"); break;
                    default: Logcat_Display.Text += (timeUTCstring + "???" + data + "\n"); break;
                }
            }
        }

        public void FldUpd(ValueSign.FieldSign field, string data)
        {
            switch(field)
            {
                case ValueSign.FieldSign.Destination: Destination_Fill.Text = data; break;
                case ValueSign.FieldSign.BufferSize: Buffer_Size.Text = data; break;
                case ValueSign.FieldSign.TTL: TTL_Count.Text = data; break;
                case ValueSign.FieldSign.TimeOut: Timeout_Count.Text = data; break;
                case ValueSign.FieldSign.Interval: Interval_Count.Text = data; break;
                default: StdUpd(ValueSign.StatusSign.Error, "WTH"); break;
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

        public Int32 CheckNumberBetween(Int32 input, Int32 min, Int32 max)
        {
            Int32 result = input;
            if (result >= min && result <= max)
                return result;
            else
                return -1;
        }

        public Int32 CheckNumberLarger(Int32 input,Int32 floor)
        {
            Int32 result = input;
            if (result >= floor)
                return result;
            else
                return -1;
        }

        private void UIElementEnabler(bool enabler)
        {
            Destination_Fill.IsEnabled = enabler;
            Buffer_Size.IsEnabled = enabler;
            Is_DF.IsEnabled = enabler;
            TTL_Count.IsEnabled = enabler;
            Timeout_Count.IsEnabled = enabler;
            Interval_Count.IsEnabled = enabler;
            Start_Button.IsEnabled = enabler;
            Stop_Button.IsEnabled = !enabler;
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
                default: return "Unknown Error";
            }
        }

        public void ExceptionLogcat(Exception exception)
        {
            StatusUpdater statusUpdater = StdUpd;
            string exceptioninformation = "----EXCEPTION----\n";
            if (exception.Message != null)
                exceptioninformation += ("==MESSAGE==\n" + exception.Message + "\n");
            if (exception.InnerException != null)
                exceptioninformation += ("==INNER EXCEPTION==\n" + exception.InnerException.ToString() + "\n");
            if (exception.Source != null)
                exceptioninformation += ("==Source==\n" + exception.Source + "\n");
            if (exception.TargetSite != null)
                exceptioninformation += ("==TARGET SITE==\n" + exception.TargetSite.ToString() + "\n");
            if (exception.Data != null)
                exceptioninformation += ("==DATA==\n" + exception.Data.ToString() + "\n");
            if (exception.StackTrace != null)
                exceptioninformation += ("==STACK TRACE==\n" + exception.StackTrace + "\n");
            if (exception.HelpLink != null)
                exceptioninformation += ("==HELP LINK==\n" + exception.HelpLink + "\n");
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Exception, exceptioninformation);
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
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Important, "Statring validation...");
            //Valid IP or domain name
            if (Destination_Fill.Text != "")
            {
                dest = Destination_Fill.Text;
                checker = (byte)(checker | (byte)0x01);
            }
            else
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Error, "No destination filled.");
            //Valid buffer size
            if (Buffer_Size.Text != "")
                if (CheckANumber(Buffer_Size.Text))
                    if ((bufferlength = CheckNumberBetween(Convert.ToInt32(Buffer_Size.Text), 32, 65500)) != -1)
                        checker = (byte)(checker | (byte)0x02);
                    else
                        Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Error, "Invalid buffer size setting.");
                else
                    Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Error, "Invalid buffer size Input.");
            else
            {
                Dispatcher.Invoke(fieldUpdater, ValueSign.FieldSign.BufferSize, bufferlength.ToString());
                checker = (byte)(checker | (byte)0x02);
            }
            //Valid TTL
            if (TTL_Count.Text != "")
                if (CheckANumber(TTL_Count.Text))
                    if ((ttlvalue = CheckNumberBetween(Convert.ToInt32(TTL_Count.Text), 1, 255)) != -1)
                        checker = (byte)(checker | (byte)0x04);
                    else
                        Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Error, "Invalid TTL Value.");
                else
                    Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Error, "Invalid TTL Input.");
            else
            {
                Dispatcher.Invoke(fieldUpdater, ValueSign.FieldSign.TTL, ttlvalue.ToString());
                checker = (byte)(checker | (byte)0x04);
            }
            //Valid Timeout
            if (Timeout_Count.Text != "")
                if (CheckANumber(Timeout_Count.Text))
                    if ((timeout = CheckNumberLarger(Convert.ToInt32(Timeout_Count.Text), 1)) != -1)
                        checker = (byte)(checker | (byte)0x08);
                    else { }
                else
                    Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Error, "Invalid timeout input.");
            else
            {
                Dispatcher.Invoke(fieldUpdater, ValueSign.FieldSign.TimeOut, timeout.ToString());
                checker = (byte)(checker | (byte)0x08);
            }
            //Valid interval
            if (Interval_Count.Text != "")
                if (CheckANumber(Interval_Count.Text))
                    if ((interval = CheckNumberLarger(Convert.ToInt32(Interval_Count.Text), 1)) != -1)
                        checker = (byte)(checker | (byte)0x10);
                    else { }
                else
                    Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Error, "Invalid interval input.");
            else
            {
                Dispatcher.Invoke(fieldUpdater, ValueSign.FieldSign.Interval, interval.ToString());
                checker = (byte)(checker | (byte)0x10);
            }
            //Check Valid
            if (checker == 0x1F)
            {
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Information, "Validation completed, preparing...");
                DataSct.PingParam pingparamdata = new DataSct.PingParam
                {
                    destination = dest,
                    buffersize = bufferlength,
                    dflag = Is_DF.IsChecked.Value,
                    ttl = ttlvalue,
                    timeout = timeout,
                    interval = interval
                };
                UIElementEnabler(false);
                pingworker = new Thread(new ParameterizedThreadStart(PingerLauncher))
                {
                    IsBackground = true,
                    Name = "Ping Worker",
                    Priority = ThreadPriority.AboveNormal
                };
                controlon.Reset();
                pingworker.Start((object)pingparamdata);
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Information, "Ping worker launcher has been called.");
            }
            else
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Information, "Validation failed for wrong parameters.");
        }

        private void Stop_Button_Click(object sender, RoutedEventArgs e)
        {
            StatusUpdater statusUpdater = StdUpd;
            if (pingworker.IsAlive)
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Important, "Calling ping worker launcher to the hell...");
            controlon.Set();
            UIElementEnabler(true);
        }

        private void MainWindowClosed(object sender, EventArgs e)
        {
            controlon.Set();
            alwayson.Set();
        }

        private void Logcat_Display_TextChanged(object sender, TextChangedEventArgs e)
        {
            Logcat_Display.ScrollToEnd();
        }
    }
}
