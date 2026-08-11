using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;

namespace AutoPingCsharp_Net
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public class DataSct
    {
        public struct PingParam
        {
            public string destination;
            public IPAddress destinationaddress;
            public string resolvedhostname;
            public int buffersize;
            public bool dflag;
            public int ttl;
            public int timeout;
            public int interval;
        }
    }

    public class ValueSign
    {
        public enum StatusSign
        {
            NetworkAvailability = 1,
            Loopback = 2,
            SmoothPing = 3,
            PackageLoss = 4,
            CurrentPing = 5,
            Exception = -1,
            Error = -2,
            Warning = -3,
            Important = -4,
            Information = -5
        };

        public enum FieldSign
        {
            Destination = 1,
            BufferSize = 2,
            TTL = 3,
            TimeOut = 4,
            Interval = 5
        };
    }

    public partial class MainWindow : Window
    {
        public delegate void StatusUpdater(ValueSign.StatusSign updatefield, string data);
        public delegate void FieldUpdater(ValueSign.FieldSign updatefield, string data);

        private Thread localcheck;
        private Thread networkstate;
        private Thread pingworker;
        public static int default_buffer = 32, default_ttl = 64, default_timeout = 5000, default_interval = 1000, default_timewindow = 120000,
            default_networkcheckinterval = 4000, default_loopbackcheckinterval = 4000;
        private readonly List<long?> rtt = [];
        private int statisticpackcount, sentpackcount, successpackcount, failedpackcount;
        private long totalrtt;
        private readonly EventWaitHandle alwayson = new(false, EventResetMode.ManualReset);
        private readonly EventWaitHandle controlon = new(false, EventResetMode.ManualReset);
        private readonly Lock totalaccesslock = new();
        private readonly Lock statisticaccesslock = new();
        private readonly Lock statusrefreshlock = new();

        public MainWindow()
        {
            InitializeComponent();
            InitWorker();
        }

        public void InitWorker()
        {
            Logcat_Display.Text = string.Empty;
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
            int loopbackcheckinterval = default_loopbackcheckinterval;
            StatusUpdater statusUpdater = StdUpd;
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Important, "Loopback check launcher started, check interval: " + loopbackcheckinterval.ToString("0ms"));
            Timer timer = new(LocalTest, new AutoResetEvent(false), 0, loopbackcheckinterval);
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Information, "Loopback check lighter started.");
            alwayson.WaitOne();
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Warning, "Loopback check launcher dying.");
            timer.Dispose(alwayson);
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Warning, "Loopback check lighter broken.");
        }

        public void LocalTest(object sender)
        {
            Ping loopbackping = new();
            bool loopv4, loopv6;
            StatusUpdater statusUpdate = StdUpd;
            PingReply rplv4 = loopbackping.Send("127.0.0.1", 100);
            PingReply rplv6 = loopbackping.Send("::1", 100);
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
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Important, "Ping worker launcher started.");
            if (pingparam is not DataSct.PingParam pingparamdata)
            {
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Error, "Invalid ping parameter payload.");
                return;
            }

            bool pingallowed;
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Important, "Fetching Address...");
            DataSct.PingParam receivehostentry = FetchHostAddress(pingparamdata.destination);
            pingparamdata.resolvedhostname = receivehostentry.resolvedhostname;
            pingparamdata.destinationaddress = receivehostentry.destinationaddress;
            if (pingparamdata.destinationaddress != null)
                pingallowed = true;
            else
                pingallowed = false;
            if (pingallowed)
            {
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Information, "Destination check OK.");
                controlon.Reset();
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
                    rtt.Clear();
                }
                Timer timer = new(Pinger, pingparamdata, 0, pingparamdata.interval);
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Information, "Ping worker lighter started.");
                controlon.WaitOne();
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Warning, "Ping worker launcher dying.");
                timer.Dispose(controlon);
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Warning, "Ping worker lighter broken.");
            }
            else
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Error, "Network error or wrong destination, ping worker launcher dying.");
        }

        public void Pinger(object pingparam)
        {
            DataSct.PingParam pingparamdata = (DataSct.PingParam)pingparam;
            Ping pingwork = new();
            StatusUpdater statusUpdater = StdUpd;
            string destinationdisplay;
            string destinationresolvedname = pingparamdata.resolvedhostname ?? pingparamdata.destination;
            string destinationaddress = pingparamdata.destinationaddress?.ToString() ?? string.Empty;
            if (string.Compare(destinationresolvedname, destinationaddress, StringComparison.OrdinalIgnoreCase) != 0)
                destinationdisplay = destinationresolvedname + " (" + destinationaddress + ")";
            else
                destinationdisplay = destinationaddress;
            try
            {
                PingReply pingReply = pingwork.Send(pingparamdata.destinationaddress, pingparamdata.timeout, new byte[pingparamdata.buffersize], new PingOptions(pingparamdata.ttl, pingparamdata.dflag));
                lock (totalaccesslock)
                {
                    if (sentpackcount < statisticpackcount)
                        sentpackcount += 1;
                }
                if (pingReply.Status == IPStatus.Success)
                {
                    PackCounter(true, pingReply.RoundtripTime);
                    Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.CurrentPing, pingReply.RoundtripTime.ToString("0ms"));
                    if (((pingReply.RoundtripTime > pingparamdata.interval) && (pingparamdata.interval >= 500)) || (pingReply.RoundtripTime >= 1000))
                        Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Warning, destinationdisplay + " ICMP reply latecy too long: " + pingReply.RoundtripTime.ToString("0ms"));
                }
                else
                {
                    PackCounter(false, null);
                    Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Error, destinationdisplay + " " + ICMPErrorAnalysis(pingReply.Status));
                    Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.CurrentPing, "Failed");
                }
            }
            catch (Exception exception)
            {
                ExceptionLogcat(exception);
            }
            double averagepingtime, packetlossrate;
            lock (statisticaccesslock)
            {
                averagepingtime = successpackcount > 0 ? (double)totalrtt / successpackcount : -1;
                lock (totalaccesslock)
                {
                    packetlossrate = sentpackcount > 0 ? (double)failedpackcount / sentpackcount : -1;
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
            if (pingworker?.IsAlive != true)
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Error, "Ping worker launcher died.");
        }

        public void PackCounter(bool issuccess, long? roundtriptime)
        {
            lock (statisticaccesslock)
            {
                if (statisticpackcount > 0 && rtt.Count >= statisticpackcount)
                {
                    if (rtt[0] is long previousRtt)
                    {
                        successpackcount -= 1;
                        totalrtt -= previousRtt;
                    }
                    else
                        failedpackcount -= 1;
                    rtt.RemoveAt(0);
                }
                if (issuccess && roundtriptime is long currentRtt)
                {
                    successpackcount += 1;
                    totalrtt += currentRtt;
                }
                else
                    failedpackcount += 1;
                rtt.Add(roundtriptime);
            }
        }

        public void NetworkStateLauncher()
        {
            Int32 networkcheckinterval = default_networkcheckinterval;
            StatusUpdater statusUpdater = StdUpd;
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Important, "Network availability check launcher started, interval: " + networkcheckinterval.ToString("0ms"));
            Timer timer = new(NetworkState, new AutoResetEvent(false), 0, networkcheckinterval);
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Information, "Network availability check lighter started.");
            alwayson.WaitOne();
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Warning, "Network availability check launcher dying.");
            timer.Dispose(alwayson);
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Warning, "Network availability check lighter broken.");
        }

        public void NetworkState(object sender)
        {
            StatusUpdater statusUpdater = StdUpd;
            if (networkstate?.IsAlive == true)
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

        public DataSct.PingParam FetchHostAddress(string sourceinput)
        {
            IPAddress addressreturn = null;
            string resolvedhostname = null;
            try
            {
                if (!IPAddress.TryParse(sourceinput, out addressreturn))
                {
                    IPAddress[] addresspending = Dns.GetHostAddresses(sourceinput);
                    if (addresspending is { Length: > 0 })
                    {
                        for (int i = 0; i < addresspending.Length; i++)
                        {
                            Ping ping = new();
                            PingReply pingReply = ping.Send(addresspending[i], 500);
                            if (pingReply.Status != IPStatus.BadDestination)
                            {
                                addressreturn = addresspending[i];
                                break;
                            }

                            addressreturn ??= addresspending[0];
                        }

                        if (addressreturn is not null)
                            resolvedhostname = Dns.GetHostEntry(addressreturn).HostName;
                    }
                }
            }
            catch (Exception exception)
            {
                ExceptionLogcat(exception);
            }
            return new DataSct.PingParam { destinationaddress = addressreturn, resolvedhostname = resolvedhostname };
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
                    case ValueSign.StatusSign.Exception: Logcat_Display.Text += (timeUTCstring + " X " + data + "\n"); break;
                    case ValueSign.StatusSign.Error: Logcat_Display.Text += (timeUTCstring + " E " + data + "\n"); break;
                    case ValueSign.StatusSign.Warning: Logcat_Display.Text += (timeUTCstring + " W " + data + "\n"); break;
                    case ValueSign.StatusSign.Important: Logcat_Display.Text += (timeUTCstring + " O " + data + "\n"); break;
                    case ValueSign.StatusSign.Information: Logcat_Display.Text += (timeUTCstring + " I " + data + "\n"); break;
                    default: Logcat_Display.Text += (timeUTCstring + "???" + data + "\n"); break;
                }
            }
        }

        public void FldUpd(ValueSign.FieldSign field, string data)
        {
            switch (field)
            {
                case ValueSign.FieldSign.Destination: Destination_Fill.Text = data; break;
                case ValueSign.FieldSign.BufferSize: Buffer_Size.Text = data; break;
                case ValueSign.FieldSign.TTL: TTL_Count.Text = data; break;
                case ValueSign.FieldSign.TimeOut: Timeout_Count.Text = data; break;
                case ValueSign.FieldSign.Interval: Interval_Count.Text = data; break;
                default: StdUpd(ValueSign.StatusSign.Error, "WTH"); break;
            }
        }

        public static bool CheckANumber(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            foreach (char character in input)
            {
                if (!char.IsDigit(character))
                    return false;
            }

            return true;
        }

        public static int CheckNumberBetween(int input, int min, int max)
        {
            return input >= min && input <= max ? input : -1;
        }

        public static int CheckNumberLarger(int input, int floor)
        {
            return input >= floor ? input : -1;
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

        public static string ICMPErrorAnalysis(IPStatus iPStatus)
        {
            return iPStatus switch
            {
                IPStatus.Success => "Ping OK",
                IPStatus.BadDestination => "Destination cannot receive echo or this is not a proper address",
                IPStatus.BadHeader => "The header is invalid",
                IPStatus.BadOption => "The ping option is invalid",
                IPStatus.BadRoute => "No valid route between you and the destination",
                IPStatus.DestinationHostUnreachable => "Destination unreachable",
                IPStatus.DestinationNetworkUnreachable => "Destination with its network unreachable",
                IPStatus.DestinationPortUnreachable => "Destination port unreachable",
                /*IPStatus.DestinationProhibited => "Destination prohibited";*/
                /*IPStatus.DestinationProtocolUnreachable => "Destination protocol unreachale";*/
                (IPStatus)11004 => "Destination protocol unreachable (IPv4) or Destination prohibited (IPv6) for configure reason",
                IPStatus.DestinationScopeMismatch => "Destination Scope Mismatch",
                IPStatus.DestinationUnreachable => "Destination unreachablefor unknown reason",
                IPStatus.HardwareError => "Hardware error",
                IPStatus.IcmpError => "ICMP error",
                IPStatus.NoResources => " No sufficient network resources",
                IPStatus.PacketTooBig => "Package too big",
                IPStatus.ParameterProblem => "Somewhere cannot read the header properly",
                IPStatus.SourceQuench => " Packet discarded because of you have not enough network queue or the destination failed to process",
                IPStatus.TimedOut => "Timed out",
                IPStatus.TimeExceeded => "TTL zeroed",
                IPStatus.TtlExpired => "TTL expired",
                IPStatus.TtlReassemblyTimeExceeded => "Some of fragments lost",
                IPStatus.UnrecognizedNextHeader => "Not a readable TCP or UDP indicator",
                IPStatus.Unknown => "Unknoun reason",
                _ => "Unknown Error",
            };
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
            string dest = string.Empty;
            int bufferlength = default_buffer;
            int ttlvalue = default_ttl;
            int timeout = default_timeout;
            int interval = default_interval;
            byte checker = 0x00;    //A bitfield checker
            FieldUpdater fieldUpdater = FldUpd;
            StatusUpdater statusUpdater = StdUpd;
            Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Important, "Starting validation...");
            //Valid IP or domain name
            if (!string.IsNullOrWhiteSpace(Destination_Fill.Text))
            {
                dest = Destination_Fill.Text;
                checker = (byte)(checker | (byte)0x01);
            }
            else
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Error, "No destination filled.");
            //Valid buffer size
            if (!string.IsNullOrWhiteSpace(Buffer_Size.Text))
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
            if (!string.IsNullOrWhiteSpace(TTL_Count.Text))
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
            if (!string.IsNullOrWhiteSpace(Timeout_Count.Text))
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
            if (!string.IsNullOrWhiteSpace(Interval_Count.Text))
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
                DataSct.PingParam pingparamdata = new()
                {
                    destination = dest,
                    buffersize = bufferlength,
                    dflag = Is_DF.IsChecked ?? false,
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
                pingworker.Start(pingparamdata);
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Information, "Ping worker launcher has been called.");
            }
            else
                Dispatcher.Invoke(statusUpdater, ValueSign.StatusSign.Information, "Validation failed for wrong parameters.");
        }

        private void Stop_Button_Click(object sender, RoutedEventArgs e)
        {
            StatusUpdater statusUpdater = StdUpd;
            if (pingworker?.IsAlive == true)
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
