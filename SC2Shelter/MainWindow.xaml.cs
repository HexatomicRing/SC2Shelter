using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.IO;
using System.Windows.Forms;
using CheckBox = System.Windows.Controls.CheckBox;
using Path = System.IO.Path;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NLog;

namespace SC2Shelter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// 日志
        /// </summary>
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private static readonly (string, string, bool)[] Langs =
        {
            ("zhCN", "简体中文", true),
            ("zhTW", "繁体中文", true),
            ("deDE", "德语", true),
            ("enUS", "英语", true),
            ("esMX", "西班牙语(墨西哥)", true),
            ("esES", "西班牙语(西班牙)", true),
            ("frFR", "法语", true),
            ("itIT", "意大利语", true),
            ("plPL", "波兰语", true),
            ("ptBR", "葡萄牙语(巴西)", true),
            ("ruRU", "俄语", true),
            ("koKR", "朝鲜语(南朝鲜)", true)
        };
        private const string Save = "latest.list";
        private static readonly object LockerFile = new object();
        private static readonly object LockerConsole = new object();
        private readonly List<CheckBox> _langBoxes = new List<CheckBox>();
        private const string CacheDir = @"C:\ProgramData\Blizzard Entertainment\Battle.net\Cache\";
        private long _version;
        private bool _needRefresh;
        private readonly SolidColorBrush _brushRed = new SolidColorBrush(Color.FromArgb(255, 255, 182, 193));
        private readonly SolidColorBrush _brushYellow = new SolidColorBrush(Color.FromArgb(255, 255, 255, 180));
        private readonly SolidColorBrush _brushGreen = new SolidColorBrush(Color.FromArgb(255, 180, 255, 180));
        private const string StateSafe = "安全，已锁住带有\n链接的地图信息";
        private const string StateWarn = "仅勾选的语言安全";
        private const string StateUnsafe = "不安全，游戏可能卡死!\n请检先关闭游戏,等到显示安全再启动。";
        private readonly List<(string, string)> _blockList = new List<(string, string)>();

        private NotifyIcon _notifyIcon;
        private readonly FileSystemWatcher fileSystemWatcher;
        /// <summary>
        /// 文件名匹配正则表达式
        /// </summary>
        private readonly Regex FILE_FULL_PATH_REGEX = new Regex(".s2ml$");
        /// <summary>
        /// 文件内容匹配正则表达式
        /// </summary>
        private readonly Regex FILE_CONTENT_REGEX = new Regex(".*path=.*");
        private static readonly Dictionary<string, FileStream> LockedFiles = new Dictionary<string, FileStream>();

        private static bool LockFile(string filePath)
        {
            lock (LockerFile)
            {
                if (LockedFiles.ContainsKey(filePath)) return true;
                FileStream fileStream = null;
                try
                {
                    var directoryPath = Path.GetDirectoryName(filePath);
                    if (!Directory.Exists(directoryPath) && directoryPath != null) Directory.CreateDirectory(directoryPath);
                    if (!File.Exists(filePath)) File.Create(filePath).Close();
                    fileStream = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    fileStream.Lock(0, 1);
                    LockedFiles[filePath] = fileStream;
                    return true;
                }
                catch (Exception ex)
                {
                    fileStream?.Dispose();
                    logger.Error(ex, $"锁定文件 {filePath} 出现未知错误");
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
                catch (Exception ex)
                {
                    logger.Error(ex, $"解锁文件 {filePath} 出现未知错误");
                    return false;
                }
                finally
                {
                    fileStream?.Dispose();
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            InitNotifyIcon();
            Closing += MainWindow_Close;
            MinimizeToTray.Click += MinimizeToTray_Click;
            AddCheckboxes();
            ReadSaving();
            RunAsyncTask();
            UpdateList();
            fileSystemWatcher = new FileSystemWatcher
            {
                Path = CacheDir,
                NotifyFilter = NotifyFilters.FileName,
                IncludeSubdirectories = true
            };
            fileSystemWatcher.Created += FileSystemWatcher_Created;
            fileSystemWatcher.EnableRaisingEvents = true;
        }

        private async void FileSystemWatcher_Created(object sender, FileSystemEventArgs e)
        {
            logger.Info($"文件 {e.FullPath} 已经新增");
            try
            {
                if (FILE_FULL_PATH_REGEX.IsMatch(e.FullPath))
                {
                    bool isMatch;
                    using (FileStream fs = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        using (StreamReader sr = new StreamReader(fs))
                        {
                            var res = await sr.ReadToEndAsync();
                            isMatch = FILE_CONTENT_REGEX.IsMatch(res);
                        }
                    }
                    if (isMatch)
                    {
                        File.Delete(e.FullPath);
                        logger.Info($"文件 {e.FullPath} 已删除");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"监视文件 {e.FullPath} 出现异常");
            }
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

        private void AddCheckboxes()
        {
            foreach (var (_, text, state) in Langs)
            {
                var checkBox = new CheckBox
                {
                    Content = text,
                    IsChecked = state
                };
                checkBox.Checked += Checked;
                checkBox.Unchecked += Unchecked;
                LangPanel.Children.Add(checkBox);
                _langBoxes.Add(checkBox);
            }
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

        private void Checked(object sender, RoutedEventArgs e)
        {
            var index = _langBoxes.IndexOf((CheckBox)sender);
            if (index >= 0)
            {
                var (id, text, _) = Langs[index];
                Langs[index] = (id, text, true);
            }
            _needRefresh = true;
        }

        private void Unchecked(object sender, RoutedEventArgs e)
        {
            var index = _langBoxes.IndexOf((CheckBox)sender);
            if (index >= 0)
            {
                var (id, text, _) = Langs[index];
                Langs[index] = (id, text, false);
            }
            _needRefresh = true;
        }

        private bool LangLock(string lang)
        {
            foreach (var (id, _, state) in Langs)
            {
                if (id == lang) return state;
            }
            return false;
        }

        private async void RunAsyncTask()
        {
            await Task.Run(() =>
                {
                    while (true)
                    {
                        if (_needRefresh)
                        {
                            var safe = true;
                            var lockCount = 0;
                            var unlockCount = 0;
                            var failLock = 0;
                            var failUnlock = 0;
                            foreach (var (name, lang) in _blockList)
                            {
                                var path = CacheDir + name;
                                var toLock = LangLock(lang);
                                if (!LockedFiles.ContainsKey(path))
                                {
                                    if (toLock)
                                    {
                                        var result = LockFile(CacheDir + name);
                                        if (result)
                                        {
                                            lockCount++;
                                        }
                                        else
                                        {
                                            failLock++;
                                            safe = false;
                                        }
                                    }
                                }
                                else
                                {
                                    if (!toLock)
                                    {
                                        var result = UnlockFile(CacheDir + name);
                                        if (result)
                                        {
                                            unlockCount++;
                                        }
                                        else
                                        {
                                            failUnlock++;
                                        }
                                    }
                                }
                            }

                            var info = "文件状态更新完毕";
                            if (lockCount > 0) info += $"，{lockCount}个文件被锁定";
                            if (failLock > 0) info += $"，{failLock}个文件锁定失败";
                            if (unlockCount > 0) info += $"，{unlockCount}个文件被解锁";
                            if (failUnlock > 0) info += $"，{failUnlock}个文件解锁失败";
                            if (lockCount == 0 && failLock == 0 && unlockCount == 0 && failUnlock == 0)
                                info += "，没有文件发生状态更新。";
                            else
                                info += "。";
                            Print(info);
                            _needRefresh = false;
                            Dispatcher.Invoke(() =>
                            {
                                if (safe)
                                {
                                    var lockAll = true;
                                    foreach (var (_, _, state) in Langs)
                                    {
                                        if (!state)
                                        {
                                            lockAll = false;
                                            break;
                                        }
                                    }

                                    if (lockAll)
                                    {
                                        StateLabel.Background = _brushGreen;
                                        StateLabel.Content = StateSafe;
                                    }
                                    else
                                    {
                                        StateLabel.Background = _brushYellow;
                                        StateLabel.Content = StateWarn;
                                    }
                                }
                                else
                                {
                                    StateLabel.Background = _brushRed;
                                    StateLabel.Content = StateUnsafe;
                                }
                            });

                        }
                        Task.Delay(50).Wait();
                    }
                }
            );
        }

        private async void UpdateList()
        {
            //This function is hidden in the open-source version.
        }

        private void ReadSaving()
        {
            try
            {
                var buffer = new List<(string, string)>();
                foreach (var line in File.ReadAllLines(Save))
                {
                    var pars = line.Split(';');
                    buffer.Add((pars[0], pars[1]));
                }
                _blockList.Clear();
                foreach (var pair in buffer)
                {
                    _blockList.Add(pair);
                }
                _needRefresh = true;
            }
            catch
            {
                // ignored
            }
        }
    }
}