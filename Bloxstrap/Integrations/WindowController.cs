using System.Windows;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Drawing2D;

public struct Rect {
   public int Left { get; set; }
   public int Top { get; set; }
   public int Right { get; set; }
   public int Bottom { get; set; }
}

namespace Bloxstrap.Integrations
{
    public class WindowController : IDisposable
    {
        private const string LOG_IDENT = "WindowController::OnMessage";

        private readonly ActivityWatcher _activityWatcher;
        private IntPtr _currentWindow;
        private bool _foundWindow = false;
        public const uint WM_SETTEXT = 0x000C;

        // 1280x720 as default
        private readonly int defaultScreenSizeX = 1280;
        private readonly int defaultScreenSizeY = 720;

        private double screenSizeX;
        private double screenSizeY;

        // cache last data to prevent bloating
        private int _lastX = 0;
        private int _lastY = 0;
        private int _lastWidth = 0;
        private int _lastHeight = 0;
        private double _lastSCWidth = 0;
        private double _lastSCHeight = 0;
        private byte _lastTransparency = 1;
        private uint _lastWindowColor = 0x000000;
        private uint _lastWindowCaptionColor = 0x000000;
        private uint _lastWindowBorderColor = 0x000000;

        private int _startingX = 0;
        private int _startingY = 0;
        private int _startingWidth = 0;
        private int _startingHeight = 0;

        private const int SW_MAXIMIZE = 3;
        private const int SW_MINIMIZE = 6;
        private const int SW_RESTORE = 9;

        private const uint MB_OK = (uint) 0x00000000L;

        private string _lastPopupTitle = "";
        private int? _messagePopup;

        private Theme appTheme = Theme.Default;

        public WindowController(ActivityWatcher activityWatcher)
        {
            _activityWatcher = activityWatcher;
            _activityWatcher.OnRPCMessage += (_, message) => OnMessage(message);

            _lastSCWidth = defaultScreenSizeX;
            _lastSCHeight = defaultScreenSizeY;

            // try to find window
            _currentWindow = _FindWindow("Roblox");
            _foundWindow = !(_currentWindow == (IntPtr)0);

            if (_foundWindow) 
                onWindowFound(); 
            
            screenSizeX = SystemParameters.PrimaryScreenWidth;
            screenSizeY = SystemParameters.PrimaryScreenHeight;
        }

        public void onWindowFound() {
            Rect winRect = new Rect();
            GetWindowRect(_currentWindow, ref winRect);    
            _lastX = winRect.Left;
            _lastY = winRect.Top;
            _lastWidth = winRect.Right - winRect.Left;
            _lastHeight = winRect.Bottom - winRect.Top;

            _startingX = _lastX;
            _startingY = _lastY;
            _startingWidth = _lastWidth;
            _startingHeight = _lastHeight;

            appTheme = ThemeEx.GetFinal(App.Settings.Prop.Theme);
            if (App.Settings.Prop.CanGameChangeColor && appTheme == Theme.Dark)
            {
                _lastWindowCaptionColor = Convert.ToUInt32("1F1F1F", 16);
                DwmSetWindowAttribute(_currentWindow, 35, ref _lastWindowCaptionColor, sizeof(int));
            }
            
            //dpi awareness
            using (Graphics graphics = Graphics.FromHwnd(_currentWindow))
            {
                screenSizeX *= graphics.DpiX / 96;
                screenSizeY *= graphics.DpiY / 96;
            }
            
            App.Logger.WriteLine("WindowController::onWindowFound", $"WinSize X:{_lastX} Y:{_lastY} W:{_lastWidth} H:{_lastHeight} sW:{screenSizeX} sH:{screenSizeY}");
        }

        public void resetWindow() {
            _lastX = _startingX;
            _lastY = _startingY;
            _lastWidth = _startingWidth;
            _lastHeight = _startingHeight;

            // TODO: maybe not reset scaling props?
            _lastSCWidth = defaultScreenSizeX;
            _lastSCHeight = defaultScreenSizeY;

            _lastTransparency = 1;
            _lastWindowColor = 0x000000;

            if (_messagePopup is not null) {
                IntPtr _popupHandle = FindWindow(null, _lastPopupTitle);
                bool _foundPopup = !(_popupHandle == (IntPtr)0);

                if (_foundPopup) {
                    CloseWindow(_popupHandle);
                }

                _messagePopup = null;
            }

            MoveWindow(_currentWindow,_startingX,_startingY,_startingWidth,_startingHeight,false);
            SetWindowLong(_currentWindow, -20, 0x00000000);
            ShowWindow(_currentWindow, SW_MAXIMIZE);

            SendMessage(_currentWindow, WM_SETTEXT, IntPtr.Zero, "Roblox");
        }

        private List<System.Windows.Forms.Form> forms = new();
        public void removeWindows() {
            // TODO: Clear the list above!!
        }

        public void OnMessage(Message message) {
            // try to find window now
            if (!_foundWindow) {
                _currentWindow = _FindWindow("Roblox");
                _foundWindow = !(_currentWindow == (IntPtr)0);

                if (_foundWindow) 
                    onWindowFound(); 
            }

            if (_currentWindow == (IntPtr)0 ) 
                return;

            switch(message.Command)
            {
                case "BeginListeningWindow": {
                    _activityWatcher.delay = _activityWatcher.windowLogDelay;
                    break;
                }
                case "StopListeningWindow": {
                    _activityWatcher.delay = 250;
                    break;
                }
                case "RestoreWindowState": case "RestoreWindow": case "ResetWindow": {
                    resetWindow();
                    break;
                }
                case "ShowPopup": {
                    if (!App.Settings.Prop.CanGameMoveWindow) 
                        break; 
                    BloxstrapPopup? popupData;

                    try
                    {
                        popupData = message.Data.Deserialize<BloxstrapPopup>();
                    }
                    catch (Exception)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization threw an exception)");
                        return;
                    }

                    if (popupData is null)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization returned null)");
                        return;
                    }

                    if (_messagePopup is not null) {
                        IntPtr _popupHandle = FindWindow(null, _lastPopupTitle);
                        bool _foundPopup = !(_popupHandle == (IntPtr)0);

                        if (_foundPopup) {
                            CloseWindow(_popupHandle);
                        }

                        _messagePopup = null;
                    }

                    string title = "";
                    string caption = "";

                    if (popupData.Title is not null) 
                        title = popupData.Title;
                    
                    if (popupData.Caption is not null) 
                        caption = popupData.Caption;
                    
                    if (title is not "") {
                        _lastPopupTitle = title;
                        Task.Run(() => {
                            _messagePopup = MessageBox(_currentWindow, title, caption, MB_OK);
                        });
                    }
                    break;
                }
                case "ShowWindow": {
                    if (!App.Settings.Prop.CanGameMoveWindow) { break; }
                    WindowShow? windowData;

                    try
                    {
                        windowData = message.Data.Deserialize<WindowShow>();
                    }
                    catch (Exception)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization threw an exception)");
                        return;
                    }

                    if (windowData is null)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization returned null)");
                        return;
                    }

                    bool _showWindow = true;
                    if (windowData.Show is not null) {
                        _showWindow = (bool) windowData.Show;
                    }

                    ShowWindow(_currentWindow, _showWindow ? SW_RESTORE : SW_MINIMIZE);
                    break;
                }
                case "CreateWindow": case "MakeWindow": {
                    if (!App.Settings.Prop.CanGameMoveWindow) { break; }
                    WindowMessage? windowData;

                    try
                    {
                        windowData = message.Data.Deserialize<WindowMessage>();
                    }
                    catch (Exception)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization threw an exception)");
                        return;
                    }

                    if (windowData is null)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization returned null)");
                        return;
                    }

                    Task.Run(() => {
                        System.Windows.Forms.Form form = new System.Windows.Forms.Form();
                        form.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
                        form.ShowInTaskbar = false;
                        form.Icon = null;

                        form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                        form.Location = new System.Drawing.Point(0, 0);

                        System.Windows.Forms.PictureBox pictureBox = new System.Windows.Forms.PictureBox();
                        pictureBox.Dock = System.Windows.Forms.DockStyle.Fill;
                        pictureBox.Load("https://cdn.discordapp.com/attachments/1092849712480129200/1290744816816095385/bounce.gif?ex=670b6b09&is=670a1989&hm=5bac6bee0e63440d8532f0c85523c294e071c5dbeb28ee0c33e299665a03f8c1&");
                        pictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;

                        Graphics gfxControl = pictureBox.CreateGraphics();
                        gfxControl.InterpolationMode = InterpolationMode.NearestNeighbor;
                        gfxControl.PixelOffsetMode = PixelOffsetMode.Half;

                        form.Controls.Add(pictureBox);

                        form.ShowDialog();
                        forms.Add(form);

                        Message msg = new Message
                        {
                            Data = message.Data,
                            Command = "SetWindow"
                        };

                        OnMessage(msg);
                    });

                    break;
                }
                case "SetWindow": {
                    if (!App.Settings.Prop.CanGameMoveWindow) { break; }
                    WindowMessage? windowData;

                    try
                    {
                        windowData = message.Data.Deserialize<WindowMessage>();
                    }
                    catch (Exception)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization threw an exception)");
                        return;
                    }

                    if (windowData is null)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization returned null)");
                        return;
                    }

                    if (windowData.Reset == true) {
                        resetWindow();
                        return;
                    }

                    System.Windows.Forms.Form? targetForm = null;
                    if (windowData.WindowID is not null && (int) windowData.WindowID >= 0) {
                        targetForm = forms.ElementAt(new Index((int) windowData.WindowID));
                    }

                    if (windowData.ScaleWidth is not null) {
                        _lastSCWidth = (double) windowData.ScaleWidth;
                    }

                    if (windowData.ScaleHeight is not null) {
                        _lastSCHeight = (double) windowData.ScaleHeight;
                    }

                    // scaling
                    float scaleX = (float) (screenSizeX / _lastSCWidth);
                    float scaleY = (float) (screenSizeY / _lastSCHeight);

                    if (windowData.X is not null) {
                        _lastX = (int) (windowData.X * scaleX);
                    }

                    if (windowData.Y is not null) {
                        _lastY = (int) (windowData.Y * scaleY);
                    }

                    if (windowData.Width is not null) {
                        _lastWidth = (int) (windowData.Width * scaleX);
                    }

                    if (windowData.Height is not null) {
                        _lastHeight = (int) (windowData.Height * scaleY);
                    }

                    if (targetForm is not null) {
                        // TODO: Fix these?
                        /*targetForm.Location = new System.Drawing.Point(_lastX, _lastY);
                        targetForm.Size = new System.Drawing.Size(_lastWidth, _lastHeight);*/
                    } else {
                        MoveWindow(_currentWindow,_lastX,_lastY,_lastWidth,_lastHeight,false);
                    }

                    //App.Logger.WriteLine(LOG_IDENT, $"Updated Window Properties");
                    break;
                }
                case "SetWindowTitle": case "SetTitle": {
                    if (!App.Settings.Prop.CanGameSetWindowTitle) {return;}

                    WindowTitle? windowData;
                    try
                    {
                        windowData = message.Data.Deserialize<WindowTitle>();
                    }
                    catch (Exception)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization threw an exception)");
                        return;
                    }

                    if (windowData is null)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization returned null)");
                        return;
                    }

                    string title = "Roblox";
                    if (windowData.Name is not null) {
                        title = windowData.Name;
                    }

                    SendMessage(_currentWindow, WM_SETTEXT, IntPtr.Zero, title);
                    break;
                }
                // save window state sounds better
                case "SaveWindowState": case "SetWindowDefault":
                    _startingX = _lastX;
                    _startingY = _lastY;
                    _startingWidth = _lastWidth;
                    _startingHeight = _lastHeight;
                    break;
                /*case "SetWindowBorder": {
                    if (!App.Settings.Prop.CanGameMoveWindow) { break; }
                    
                    Models.BloxstrapRPC.WindowBorderType? windowData;

                    try
                    {
                        windowData = message.Data.Deserialize<Models.BloxstrapRPC.WindowBorderType>();
                    }
                    catch (Exception)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization threw an exception)");
                        return;
                    }

                    if (windowData is null)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization returned null)");
                        return;
                    }

                    string borderType = "windowed";
                    if (windowData.BorderType is not null) {
                        borderType = windowData.BorderType;
                    }
                    try
                    {
                        // fucking hell it's a todo now
                        // i got rusty as hell in C# apologies
                    } catch (Exception) {
                        return;
                    }
                    
                    break;
                }*/
                case "SetWindowTransparency": {
                    if (!App.Settings.Prop.CanGameMoveWindow) {return;}
                    WindowTransparency? windowData;

                    try
                    {
                        windowData = message.Data.Deserialize<WindowTransparency>();
                    }
                    catch (Exception)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization threw an exception)");
                        return;
                    }

                    if (windowData is null)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization returned null)");
                        return;
                    }

                    if (windowData.Transparency is not null) {
                        _lastTransparency = (byte) windowData.Transparency;
                    }

                    if (windowData.Color is not null) {
                        _lastWindowColor = Convert.ToUInt32(windowData.Color, 16);
                    }

                    if (_lastTransparency == 1)
                    {
                        SetWindowLong(_currentWindow, -20, 0x00000000);
                    }
                    else
                    {
                        SetWindowLong(_currentWindow, -20, 0x00FF0000);
                        SetLayeredWindowAttributes(_currentWindow, _lastWindowColor, _lastTransparency, 0x00000001);
                    }
                    break;
                }
                case "SetWindowColor": {
                    if (!App.Settings.Prop.CanGameChangeColor) {return;}
                    WindowColor? windowData;

                    try
                    {
                        windowData = message.Data.Deserialize<WindowColor>();
                    }
                    catch (Exception)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization threw an exception)");
                        return;
                    }

                    if (windowData is null)
                    {
                        App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization returned null)");
                        return;
                    }

                    if (windowData.Reset == true) {
                        windowData.Caption = appTheme == Theme.Dark ? "1F1F1F" : "FFFFFF";
                        windowData.Border = "1F1F1F";
                        windowData.Reset = false;
                    }

                   if (windowData.Caption is not null) {
                        _lastWindowCaptionColor = Convert.ToUInt32(windowData.Caption, 16);
                        DwmSetWindowAttribute(_currentWindow, 35, ref _lastWindowCaptionColor, sizeof(int));
                    }

                    if (windowData.Border is not null) {
                        _lastWindowBorderColor = Convert.ToUInt32(windowData.Border, 16);
                        DwmSetWindowAttribute(_currentWindow, 34, ref _lastWindowBorderColor, sizeof(int));
                    }

                    break;
                }
                default: {
                    return;
                }
            }
        }
        public void Dispose()
        {
            resetWindow();
            removeWindows();

            GC.SuppressFinalize(this);
        }

        private IntPtr _FindWindow(string title)
        {
            Process[] tempProcesses = Process.GetProcesses();
            foreach (Process proc in tempProcesses)
            {
                if (proc.MainWindowTitle == title)
                {
                    return proc.MainWindowHandle;
                }
            }
            return (IntPtr)0;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        static extern int CloseWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern int MessageBox(IntPtr? hWnd, string lpText, string lpCaption, uint uType);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, ref Rect rectangle);
        
        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hWnd, int dwAttribute, ref uint pvAttribute, int cbAttribute);
    }
}
