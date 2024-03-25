using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using System.Windows.Forms;
using Path = System.IO.Path;

namespace SC2Shelter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly (string, string, bool)[] langs =
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
        private const string save = "latest.list";
        private static readonly object lockerFile = new object();
        private static readonly object lockerConsole = new object();
        private List<System.Windows.Controls.CheckBox> langBoxes = new List<System.Windows.Controls.CheckBox>();
        private const string cacheDir = "C:/ProgramData/Blizzard Entertainment/Battle.net/Cache/";
        private long version = 0L;
        private bool NeedRefresh = false;
        private readonly SolidColorBrush brushRed = new SolidColorBrush(Color.FromArgb(255, 255, 182, 193));
        private readonly SolidColorBrush brushYellow = new SolidColorBrush(Color.FromArgb(255, 255, 255, 180));
        private readonly SolidColorBrush brushGreen = new SolidColorBrush(Color.FromArgb(255, 180, 255, 180));
        private const string stateSafe = "安全，已锁住带有\n链接的地图信息";
        private const string StateWarn = "仅勾选的语言安全";
        private const string stateUnsafe = "不安全，游戏可能卡死!\n请检先关闭游戏,等到显示安全再启动。";
        private readonly List<(string, string)> blockList = new List<(string, string)>();

        private NotifyIcon notifyIcon;

        private static readonly Dictionary<string, FileStream> LockedFiles = new Dictionary<string, FileStream>();
        private static bool LockFile(string filePath)
        {
            lock (lockerFile)
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
        private bool UnlockFile(string filePath)
        {
            lock (lockerFile)
            {
                if (!LockedFiles.ContainsKey(filePath)) return true;
                try
                {
                    var fileStream = LockedFiles[filePath];
                    fileStream.Unlock(0, 1);
                    fileStream.Dispose();
                    LockedFiles.Remove(filePath);
                    return true;
                }
                catch (IOException e)
                {
                    return false;
                }
            }
        }
        public MainWindow()
        {
            InitializeComponent();
            InitNotifyIcon();
            this.StateChanged += MainWindow_StateChanged; ;
            AddCheckboxes();
            ReadSaving();
            RunAsyncTask();
            UpdateList();
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.Hide();
                this.ShowInTaskbar = false;
            }
        }

        public void InitNotifyIcon()
        {
            notifyIcon = new NotifyIcon
            {
                Text = "星际争霸2防炸图器运行中",
                Visible = true,
                Icon = new System.Drawing.Icon("SC2Shelter.ico"),
            };
            var cms = new ContextMenuStrip();
            cms.Items.Add("关闭");
            cms.Items[0].Click += MainWindow_Click;
            notifyIcon.ContextMenuStrip = cms;
            notifyIcon.MouseClick += NotifyIcon_MouseClick;
        }

        private void MainWindow_Click(object sender, EventArgs e)
        {
            if (System.Windows.MessageBox.Show("是否退出程序？", "提示", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                Environment.Exit(0);
            }
        }

        private void NotifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (this.Visibility == Visibility.Visible)
                    return;
                this.ShowInTaskbar = true;
                this.Visibility = Visibility.Visible;
                this.Activate();
            }
        }

        private void AddCheckboxes()
        {
            foreach (var (id, text, state) in langs)
            {
                var checkBox = new System.Windows.Controls.CheckBox
                {
                    Content = text,
                    IsChecked = state
                };
                checkBox.Checked += Checked;
                checkBox.Unchecked += Unchecked;
                LangPanel.Children.Add(checkBox);
                langBoxes.Add(checkBox);
            }
        }

        private void Print(string text)
        {
            Dispatcher.Invoke(() =>
            {
                lock (lockerConsole)
                {
                    ConsoleBox.AppendText(text + "\n");
                    if (Math.Abs(ConsoleBoxViewer.ScrollableHeight - ConsoleBoxViewer.VerticalOffset) < 0.01)
                        ConsoleBoxViewer.ScrollToBottom();
                }
            });
        }

        private void Checked(object sender, RoutedEventArgs e)
        {
            var index = langBoxes.IndexOf((System.Windows.Controls.CheckBox)sender);
            if (index >= 0)
            {
                var (id, text, _) = langs[index];
                langs[index] = (id, text, true);
            }
            NeedRefresh = true;
        }

        private void Unchecked(object sender, RoutedEventArgs e)
        {
            var index = langBoxes.IndexOf((System.Windows.Controls.CheckBox)sender);
            if (index >= 0)
            {
                var (id, text, _) = langs[index];
                langs[index] = (id, text, false);
            }
            NeedRefresh = true;
        }

        private bool LangLock(string lang)
        {
            foreach (var (id, _, state) in langs)
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
                        if (NeedRefresh)
                        {
                            var safe = true;
                            var lockCount = 0;
                            var unlockCount = 0;
                            var failLock = 0;
                            var failUnlock = 0;
                            foreach (var (name, lang) in blockList)
                            {
                                var path = cacheDir + name;
                                var toLock = LangLock(lang);
                                if (!LockedFiles.ContainsKey(path))
                                {
                                    if (toLock)
                                    {
                                        var result = LockFile(cacheDir + name);
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
                                        var result = UnlockFile(cacheDir + name);
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
                            NeedRefresh = false;
                            Dispatcher.Invoke(() =>
                            {
                                if (safe)
                                {
                                    var lockAll = true;
                                    foreach (var (_, _, state) in langs)
                                    {
                                        if (!state)
                                        {
                                            lockAll = false;
                                            break;
                                        }
                                    }

                                    if (lockAll)
                                    {
                                        StateLabel.Background = brushGreen;
                                        StateLabel.Content = stateSafe;
                                    }
                                    else
                                    {
                                        StateLabel.Background = brushYellow;
                                        StateLabel.Content = StateWarn;
                                    }
                                }
                                else
                                {
                                    StateLabel.Background = brushRed;
                                    StateLabel.Content = stateUnsafe;
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
                foreach (var line in File.ReadAllLines(save))
                {
                    var pars = line.Split(';');
                    buffer.Add((pars[0], pars[1]));
                }
                blockList.Clear();
                foreach (var pair in buffer)
                {
                    blockList.Add(pair);
                }
                NeedRefresh = true;
            }
            catch
            {
                // ignored
            }
        }
    }
}