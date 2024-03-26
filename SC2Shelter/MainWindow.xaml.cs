using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Path = System.IO.Path;

namespace SC2Shelter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private const string Save = "latest.list";
        private static readonly object LockerFile = new object();
        private static readonly object LockerConsole = new object();
        private const string CacheDir = "C:/ProgramData/Blizzard Entertainment/Battle.net/Cache/";
        private long _version;
        private readonly SolidColorBrush _brushRed = new SolidColorBrush(Color.FromArgb(255, 255, 182, 193));
        private readonly SolidColorBrush _brushYellow = new SolidColorBrush(Color.FromArgb(255, 255, 255, 180));
        private readonly SolidColorBrush _brushGreen = new SolidColorBrush(Color.FromArgb(255, 180, 255, 180));
        private const string StateSafe = "安全，已锁住带有\n链接的地图信息";
        private const string StateWarn = "已锁住带链接的地图信息\n但列表可能不是最新的";
        private const string StateUnsafe = "锁定失败，游戏可能卡死!\n请检先关闭游戏,等到显示安全再启动。";
        private static readonly Dictionary<string, FileStream> LockedFiles = new Dictionary<string, FileStream>();
        private SortedSet<string> _defendList = new SortedSet<string>();
        private static readonly Regex FileContentRegex = new Regex(".*&lt;img *path *= *&quot;.*");
        private NotifyIcon _notifyIcon;
        private readonly FileSystemWatcher _fileSystemWatcher;
        private bool _connection;
        private bool _lockFail;
        public MainWindow()
        {
            InitializeComponent();
            InitNotifyIcon();
            Closing += MainWindow_Close;
            MinimizeToTray.Click += MinimizeToTray_Click;
            ScanButton.Click += ScanFiles;
            ReadSaving();
            UpdateList();
            _fileSystemWatcher = new FileSystemWatcher
            {
                Path = CacheDir,
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName,
                Filter = "*.s2ml",
                EnableRaisingEvents = true
            };
            _fileSystemWatcher.Created += FileSystemWatcher_Created;
        }
        private static bool LockFile(string filePath)
        {
            lock (LockerFile)
            {
                if (LockedFiles.ContainsKey(filePath)) return true;
                try
                {
                    var directoryPath = Path.GetDirectoryName(filePath);
                    if (!Directory.Exists(directoryPath) && directoryPath != null) Directory.CreateDirectory(directoryPath);
                    if (!File.Exists(filePath)) File.Create(filePath).Close();
                    var fileStream = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    fileStream.Lock(0, 1);
                    LockedFiles[filePath] = fileStream;
                    return true;
                }
                catch (IOException)
                {
                    return false;
                }
            }
        }
        private static bool UnlockFile(string filePath)
        {
            lock (LockerFile)
            {
                if (!LockedFiles.ContainsKey(filePath)) return true;
                FileStream fileStream = null;
                try
                {
                    fileStream = LockedFiles[filePath];
                    fileStream.Unlock(0, 1);
                    LockedFiles.Remove(filePath);
                    return true;
                }
                catch (IOException)
                {
                    return false;
                }
                finally
                {
                    fileStream?.Dispose();
                }
            }
        }
        private async void ScanFiles(object sender, RoutedEventArgs routedEventArgs)
        {
            ScanButton.IsEnabled = false;
            await Task.Run(() =>
            {
                Print("正在检索缓存!");
                if (!Directory.Exists(CacheDir)) return;
                foreach (var d1 in Directory.GetDirectories(CacheDir))
                {
                    if (d1.EndsWith("TMP")) continue;
                    if (d1.EndsWith("Download")) continue;
                    foreach (var d2 in Directory.GetDirectories(d1))
                    {
                        if (d2.EndsWith("Download")) continue;
                        foreach (var file in Directory.GetFiles(d2))
                        {
                            if (!file.EndsWith(".s2ml")) continue;
                            var path = file.Replace('\\', '/');
                            if(LockedFiles.ContainsKey(path)) continue;
                            if (!CheckFile(path)) continue;
                            LockFile(path);
                            Print($"文件 {path} 已锁定");
                            Report(path);
                        }
                    }
                }
                Print("检索完毕，可以启动游戏!");
            });
            ScanButton.IsEnabled = true;
        }
        private async void FileSystemWatcher_Created(object sender, FileSystemEventArgs e)
        {
            var path = e.FullPath.Replace('\\', '/').Replace("//", "/");
            if(LockedFiles.ContainsKey(path)) return;
            var sr = new StreamReader(path);
            try
            {
                var res = await sr.ReadToEndAsync();
                if (!FileContentRegex.IsMatch(res)) return;
                File.Delete(path);
                LockFile(path);
                Print($"文件 {path} 已锁定");
                Report(path);
            }
            finally
            {
                sr.Dispose();
            }
        }
        private static bool CheckFile(string path)
        {
            return FileContentRegex.IsMatch(File.ReadAllText(path));
        }
        private void MinimizeToTray_Click(object sender, EventArgs e)
        {
            Hide();
            _notifyIcon.Visible = true;
            ShowInTaskbar = false;
        }

        private void InitNotifyIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Text = "星际争霸2防炸图器运行中",
                Visible = true,
                Icon = new System.Drawing.Icon("SC2Shelter.ico"),
            };
            var cms = new ContextMenuStrip();
            cms.Items.Add("关闭");
            cms.Items[0].Click += MainWindow_Click;
            _notifyIcon.ContextMenuStrip = cms;
            _notifyIcon.MouseClick += NotifyIcon_MouseClick;
        }
        private void MainWindow_Close(object sender, CancelEventArgs e)
        {
            if (System.Windows.MessageBox.Show("是否退出程序？", "提示", MessageBoxButton.YesNo) == MessageBoxResult.No)
            {
                e.Cancel = true;
            }
            else
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
        }
        private static void MainWindow_Click(object sender, EventArgs e)
        {
            if (System.Windows.MessageBox.Show("是否退出程序？", "提示", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                Environment.Exit(0);
            }
        }

        private void NotifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (Visibility == Visibility.Visible)
                return;
            ShowInTaskbar = false;
            Visibility = Visibility.Visible;
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
        protected override void OnClosed(EventArgs e)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            base.OnClosed(e);
        }

        private void Print(string text)
        {
            Dispatcher.Invoke(() =>
            {
                lock (LockerConsole)
                {
                    ConsoleBox.AppendText(text + "\n");
                    if (Math.Abs(ConsoleBoxViewer.ScrollableHeight - ConsoleBoxViewer.VerticalOffset) < 0.01)
                        ConsoleBoxViewer.ScrollToBottom();
                }
            });
        }

        private void SetInfo(Brush brush, string text)
        {
            Dispatcher.Invoke(() =>
            {
                StateLabel.Background = brush;
                StateLabel.Content = text;
            });
        }
        private async void UpdateList()
        {
            await Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        var response = WebRequest.Create("########").GetResponse();
                        var stream = response.GetResponseStream();
                        var bytes = new byte[16];
                        if (stream != null)
                        {
                            var latest = 0L;
                            if (stream.Read(bytes, 0, 16) == 16)
                            {
                                for (var i = 8; i < 16; i++)
                                {
                                    latest <<= 8;
                                    latest += bytes[i];
                                }
                            }
                            stream.Close();
                            if (latest != 0L && latest != _version)
                            {
                                response = WebRequest.Create("########").GetResponse();
                                stream = response.GetResponseStream();
                                var buffer = new byte[32];
                                var bufferList = new SortedSet<string>();
                                if (bufferList == null) throw new ArgumentNullException(nameof(bufferList));
                                while(true)
                                {
                                    var name = new StringBuilder(32);
                                    if (stream.Read(buffer, 0, 32) != 32) break;
                                    foreach (var b in buffer)
                                    {
                                        name.Append($"{b:x02}");
                                    }
                                    bufferList.Add(name.ToString());
                                }
                                stream.Close();
                                _defendList = bufferList;
                                _version = latest;
                                Print("已获取最新屏蔽列表！");
                                UpdateLockedFile();
                                SaveList();
                            }
                            _connection = true;
                        }
                        else
                        {
                            _connection = false;
                        }
                    }
                    catch (Exception)
                    {
                        _connection = false;
                    }
                    try
                    {
                        var response = WebRequest.Create("########").GetResponse();
                        var stream = response.GetResponseStream();
                        var bytes = new byte[4];
                        if (stream != null)
                        {
                            var users = 0;
                            if (stream.Read(bytes, 0, 4) == 4)
                            {
                                for (var i = 0; i < 4; i++)
                                {
                                    users <<= 8;
                                    users += bytes[i];
                                }
                            }
                            Dispatcher.Invoke(() => { UsersLabel.Content = $"{users}人正在同时使用"; });
                        }
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                    if (!_lockFail)
                    {
                        if (_connection)
                        {
                            SetInfo(_brushGreen, StateSafe);
                        }
                        else
                        {
                            SetInfo(_brushYellow, StateWarn);
                        }
                    }
                    Task.Delay(5000).Wait();
                }
            });
        }

        private void ReadSaving()
        {
            try
            {
                if (!File.Exists(Save)) return;
                var buffer = File.ReadAllLines(Save);
                foreach (var line in buffer)
                {
                    if(line.Length == 64) _defendList.Add(line);
                }

                UpdateLockedFile();
            }
            catch
            {
                // ignored
            }
        }

        private void SaveList()
        {
            try
            {
                var writer = new StreamWriter(Save);
                foreach (var b in _defendList)
                {
                    writer.Write(b);
                    writer.Write("\n");
                }
                writer.Close();
                Print("保存列表成功！");
            }
            catch
            {
                                    
                Print("保存列表失败！");
            }
        }

        private static async void Report(string fullPath)
        {
            await Task.Run(() =>
            {
                try
                {
                    var name = fullPath.Substring(fullPath.Length - 69, 64);
                    var request = (HttpWebRequest)WebRequest.Create($"########?{name}");
                    request.Method = WebRequestMethods.Http.Get;
                    request.GetResponse();
                }
                catch
                {
                    //ignored
                }
            });
        }

        private void UpdateLockedFile()
        {
            var count = 0;
            var fail = 0;
            lock (LockerFile)
            {
                foreach (var b in _defendList)
                {
                    var sb = new StringBuilder(7);
                    sb.Append(CacheDir);
                    sb.Append(b.Substring(0, 2));
                    sb.Append("/");
                    sb.Append(b.Substring(2, 2));
                    sb.Append("/");
                    sb.Append(b);
                    sb.Append(".s2ml");
                    var fullPath = sb.ToString();
                    if(LockedFiles.ContainsKey(fullPath)) continue;
                    if (LockFile(fullPath)) count++;
                    else fail++;
                }
            }
            var info = "状态更新完毕";
            if (count > 0) info += $"，新锁定了{count}个文件";
            if (fail > 0) info += $"，锁定失败了{fail}个文件";
            info += "。";
            _lockFail = fail > 0;
            if (_lockFail)
            {
                SetInfo(_brushRed, StateUnsafe);
            }
            Print(info);
        }
    }
}