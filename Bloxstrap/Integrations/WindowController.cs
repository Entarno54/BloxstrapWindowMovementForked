using System.Windows;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Numerics;
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
        private readonly ActivityWatcher _activityWatcher;
        private IntPtr _currentWindow;
        private bool _foundWindow = false;
        public const uint WM_SETTEXT = 0x000C;

        // 1280x720 as default
        private double defaultScreenSizeX = 1280;
        private double defaultScreenSizeY = 720;

        private double screenSizeX = 0;
        private double screenSizeY = 0;

        // cache last data to prevent bloating
        private int _lastX = 0;
        private int _lastY = 0;
        private int _lastWidth = 0;
        private int _lastHeight = 0;
        private double _lastSCWidth = 0;
        private double _lastSCHeight = 0;
        private byte _lastTransparency = 1;
        private uint _lastWindowColor = 0x000000;

        private int _startingX = 0;
        private int _startingY = 0;
        private int _startingWidth = 0;
        private int _startingHeight = 0;

        private const int SW_MAXIMIZE = 3;
        private const int SW_MINIMIZE = 6;

        private string _lastPopupTitle = "";
        private System.Windows.Forms.MessageBox? _messagePopup;

        public WindowController(ActivityWatcher activityWatcher)
        {
            _activityWatcher = activityWatcher;
            _activityWatcher.OnRPCMessage += (_, message) => OnMessage(message);

            _lastSCWidth = defaultScreenSizeX;
            _lastSCHeight = defaultScreenSizeY;

            // try to find window
            _currentWindow = FindWindow("Roblox");
            _foundWindow = !(_currentWindow == (IntPtr)0);

            if (_foundWindow) { onWindowFound(); }
            
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
            
            //dpi awareness
            using (Graphics graphics = Graphics.FromHwnd(_currentWindow))
            {
                screenSizeX *= (double)(graphics.DpiX / 96);
                screenSizeY *= (double)(graphics.DpiY / 96);
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

            MoveWindow(_currentWindow,_startingX,_startingY,_startingWidth,_startingHeight,false);
            SetWindowLong(_currentWindow, -20, 0x00000000);
            ShowWindow(_currentWindow, SW_MAXIMIZE);

            SendMessage(_currentWindow, WM_SETTEXT, IntPtr.Zero, "Roblox");
        }

        private List<System.Windows.Forms.Form> forms = new List<System.Windows.Forms.Form>();
        public void removeWindows() {
            // TODO: Clear the list above!!
        }

        public void OnMessage(Message message) {
            const string LOG_IDENT = "WindowController::OnMessage";

            // try to find window now
            if (!_foundWindow) {
                _currentWindow = FindWindow("Roblox");
                _foundWindow = !(_currentWindow == (IntPtr)0);

                if (_foundWindow) { onWindowFound(); }
            }

            if (_currentWindow == (IntPtr)0) {return;}

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
                        // Won't work as its linked to winforms!
                        // TODO: Use the C++ api instead for popups.

                        /*IntPtr _popupHandle = FindWindow(_lastPopupTitle);
                        bool _foundPopup = !(_popupHandle == (IntPtr)0);

                        if (_foundPopup) {
                            CloseWindow(_popupHandle)
                        }*/

                        _messagePopup = null;
                    }

                    string title = "";
                    string caption = "";

                    if (popupData.Title is not null) {
                        title = (string) (popupData.Title);
                    }

                    if (popupData.Caption is not null) {
                        caption = (string) (popupData.Caption);
                    }

                    _lastPopupTitle = title;
                    System.Windows.Forms.MessageBoxButtons buttons = System.Windows.Forms.MessageBoxButtons.OK;

                    _messagePopup = System.Windows.Forms.MessageBox.Show(new Form { TopMost = true }, title, caption, buttons, System.Windows.Forms.MessageBoxIcon.None);
                    break;
                }
                case "ShowWindow": {
                    WindowHide? windowData;

                    try
                    {
                        windowData = message.Data.Deserialize<WindowHide>();
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

                    bool _hideWindow = false;
                    if (windowData.Hide is not null) {
                        _hideWindow = (bool) windowData.Hide;
                    }

                    ShowWindow(_currentWindow, SW_MAXIMIZE);
                    break;
                }
                case "MakeWindow": {
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
                        pictureBox.Load("https://cdn.discordapp.com/attachments/1223641810530730048/1292595927093088337/laughnmi.gif?ex=67044f44&is=6702fdc4&hm=e14ba362702360813bd5dded3b1c40558c756df195decede9590207bc7b502df&");
                        pictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;

                        Graphics gfxControl = pictureBox.CreateGraphics();
                        gfxControl.InterpolationMode = InterpolationMode.NearestNeighbor;
                        gfxControl.PixelOffsetMode = PixelOffsetMode.Half;

                        form.Controls.Add(pictureBox);

                        form.ShowDialog();
                        forms.Add(form);

                        Message msg = new Message();
                        msg.Data = message.Data;
                        msg.Command = "SetWindow";

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
                        targetForm = forms.ElementAt(new System.Index((int) windowData.WindowID));
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

        private IntPtr FindWindow(string title)
        {
            Process[] tempProcesses;
            tempProcesses = Process.GetProcesses();
            foreach (Process proc in tempProcesses)
            {
                if (proc.MainWindowTitle == title)
                {
                    return proc.MainWindowHandle;
                }
            }
            return (IntPtr)0;
        }

        [DllImport("user32.dll")]
        static extern int CloseWindow(IntPtr hWnd);

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
    }
}
