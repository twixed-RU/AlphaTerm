using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Timers;
using System.Windows.Forms;

namespace AlphaTerm
{
    public partial class AlphaTerm : Form
    {
        private static PrivateFontCollection pfc;
        private static Font _font;
        private static Graphics _graphics;
        private Point _vtSize                       = new Point(80, 25);
        private static Point _cursor                = new Point(0, 0);
        private static Point _char                  = new Point(13, 28);
        private static int _intensity               = 0;
        private static Brush _fgc                   = Brushes.LightGray;
        private static Brush _bgc                   = Brushes.Black;
        private System.Timers.Timer _cursorTimer = new System.Timers.Timer(250);
        private Brush[] _fgcMap;
        private Brush[] _bgcMap;
        private byte[] _buffer;
        private Brush[] _fgcBuffer;
        private Brush[] _bgcBuffer;
        private byte[] _cells;
        private Bitmap _bitmap;
        private Graphics _screen;
        private Thread _socketThread;
        private Socket _socket;

        private bool _drawing                       = false;
        private bool _cursorState                   = false;
        private bool _cursorEnabled                 = true;
        private bool _localEcho                     = false;
        private bool _crLf                          = false;
        private bool _ansi                          = true;
        private bool _connecting                    = false;
        private bool _listening                     = false;
        private string _escapeSequence              = null;
        private bool _dirty                         = false;
        private string _lastCommand                 = null;
        private string _connectHost                 = null;
        private int _connectPort                    = 23;

        private bool _usePictureBox                 = true; // we can draw directly on the form, although pricturebox perfectly center the screen albeit being slower


        public AlphaTerm(bool ansi = false, bool crlf = true, bool cursor = true, bool echo = false, int rows = 25, int cols = 80, string host = null, int port = 23)
        {
            _ansi = ansi;
            _crLf = crlf;
            _cursorEnabled = cursor;
            _localEcho = echo;
            _vtSize.X = cols;
            _vtSize.Y = rows;
            _connectHost = host;
            _connectPort = port;

            _bitmap = new Bitmap(_vtSize.X * _char.X, _vtSize.Y * _char.Y);
            _screen = Graphics.FromImage(_bitmap);
            _cells = Enumerable.Repeat<byte>(32, _vtSize.X * _vtSize.Y).ToArray();
            _buffer = _cells.Clone() as byte[];
            _fgcMap = Enumerable.Repeat<Brush>(_fgc, _vtSize.X * _vtSize.Y).ToArray();
            _fgcBuffer = _fgcMap.Clone() as Brush[];
            _bgcMap = Enumerable.Repeat<Brush>(_bgc, _vtSize.X * _vtSize.Y).ToArray();
            _bgcBuffer = _bgcMap.Clone() as Brush[];

            Stream fontStream = this.GetType().Assembly.GetManifestResourceStream("AlphaTerm.Resources.terminal_font.ttf"); // https://int10h.org/oldschool-pc-fonts/fontlist/
            byte[] fontdata = new byte[fontStream.Length];
            fontStream.Read(fontdata, 0, (int)fontStream.Length);
            fontStream.Close();
            pfc = new PrivateFontCollection();
            unsafe { fixed (byte* pFontData = fontdata) pfc.AddMemoryFont((System.IntPtr)pFontData, fontdata.Length); }
            _screen = Graphics.FromImage(_bitmap);
            _screen.FillRectangle(_bgc, 0, 0, _bitmap.Width, _bitmap.Height);
            InitializeComponent();
            canvas.Location = new Point(0, 0);
            canvas.Image = _bitmap;
            canvas.Visible = _usePictureBox;
            _cursorTimer.Elapsed += OnCursorTimer;
            _cursorTimer.Enabled = _cursorTimer.AutoReset = true;
            if (!_usePictureBox)
            {
                _graphics = CreateGraphics();
                _graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
                _graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            }
        }

        private bool IsPrintable(byte chr)
        {
            return (chr >= 32 && chr <= 126) || (chr > 127);
        }

        private char ConvertToChar(byte data)
        {
            // We need a specific unicode character mapping to render whole 256 ASCII character set with the font we're using.
            var mapping = new short[]
            {
                0x2591, // ░ 176
                0x2592, // ▒ 177
                0x2593, // ▓ 178
                0x2502, // │ 179
                0x2524, // ┤ 180
                0x00e1, // á 181
                0x00e2, // â 182
                0x00e0, // à 183
                0x2219, // ∙ 184 -- should be &copy;
                0x2563, // ╣ 185
                0x2551, // ║ 186
                0x2557, // ╗ 187
                0x255d, // ╝ 188
                0x00a2, // ¢ 189
                0x00a5, // ¥ 190
                0x2510, // ┐ 191
                0x2514, // └ 192
                0x2534, // ┴ 193
                0x252c, // ┬ 194
                0x251c, // ├ 195
                0x2500, // ─ 196
                0x253c, // ┼ 197
                0x2219, // ∙ 198 - should be "a" with squiggly
                0x2219, // ∙ 199 - should be "A" with squiggly
                0x255a, // ╚ 200
                0x2554, // ╔ 201
                0x2569, // ╩ 202
                0x2566, // ╦ 203
                0x2560, // ╠ 204
                0x2550, // ═ 205
                0x256c, // ╬ 206
                0x2219, //   207
                0x2219, //   208
                0x2219, //   209
                0x2219, //   210
                0x2219, //   211
                0x2219, //   212
                0x2219, //   213
                0x2219, //   214
                0x2219, //   215
                0x2219, //   216
                0x2518, // ┘ 217
                0x250c, // ┌ 218
                0x2588, // █ 219
                0x2584, // ▄ 220
                0x258c, // ▌ 221
                0x2590, // ▐ 222
                0x2580, // ▀ 223
                0x2219, //   224
                0x2219, //   225
                0x2219, //   226
                0x2219, //   227
                0x2219, //   228
                0x2219, //   229
                0x2219, //   230
                0x2219, //   231
                0x2219, //   232
                0x2219, //   233
                0x2219, //   234
                0x2219, //   235
                0x2219, //   236
                0x2219, //   237
                0x2219, //   238
                0x2219, //   239
                0x2219, //   240
                0x2219, //   241
                0x2219, //   242
                0x2219, //   243
                0x2219, //   244
                0x2219, //   245
                0x2219, //   246
                0x2248, // ≈ 247
                0x00b0, // ° 248
                0x2219, //   249
                0x2219, //   250
                0x221a, // √ 251
                0x207f, // ⁿ 252
                0x00b2, // ² 253
                0x25a0, // ■ 254
                0x00ca, //   255
            };
            var chr = (char)data;
            if (data > 175)
            {
                var idx = data - 176;
                if (idx <= mapping.Length)
                {
                    chr = Convert.ToChar(mapping[idx]);
                }
                else
                {
                    chr = Convert.ToChar(0x20);
                }
            }
            return chr;
        }

        private string GetLine(int lineNum = -1)
        {
            if (lineNum == -1 || lineNum >= _vtSize.Y) lineNum = _cursor.Y;
            var str = "";
            for (var i = 0; i < _vtSize.X; i++)
            {
                str += ConvertToChar(_cells[lineNum * _vtSize.X + i]);
            }
            return str;
        }

        private void SetLine(string line, int lineNum = -1)
        {
            if (String.IsNullOrEmpty(line)) line = "".PadRight(_vtSize.X);
            if (lineNum == -1 || lineNum >= _vtSize.Y) lineNum = _cursor.Y;
            for (var i = 0; i < _vtSize.X; i++)
            {
                if (i == line.Length) return;
                _cells[lineNum * _vtSize.X + i] = Convert.ToByte(line[i]);
            }
        }
        private void Reset()
        {
            _drawing = false;
            _fgcMap = Enumerable.Repeat<Brush>(_fgc, _vtSize.X * _vtSize.Y).ToArray();
            _bgcMap = Enumerable.Repeat<Brush>(_bgc, _vtSize.X * _vtSize.Y).ToArray();
            _cells = Enumerable.Repeat<byte>(32, _vtSize.X * _vtSize.Y).ToArray();
            _buffer = Enumerable.Repeat<byte>(33, _vtSize.X * _vtSize.Y).ToArray();
            _dirty = true;
        }

        private void ScrollUp()
        {
            if (_cursor.Y >= _vtSize.Y) _cursor.Y = _vtSize.Y - 1;
            Array.Copy(_cells, _vtSize.X, _cells, 0, _vtSize.X * (_vtSize.Y - 1));
            Array.Copy(Enumerable.Repeat<byte>(32, _vtSize.X).ToArray(), 0, _cells, _vtSize.X * (_vtSize.Y - 1), _vtSize.X);
        }

        private void MoveCursor(int x = 1, int y = 0, bool absolute = false)
        {
            CursorOff();
            if (absolute)
            {
                // We certainly could extract that stuff into Clamp method, but I don't think
                // it is really needed for it being used only here, two times, consecutively.
                _cursor.X = x;
                if (_cursor.X < 0) _cursor.X = 0;
                if (_cursor.X >= _vtSize.X) _cursor.X = _vtSize.X - 1;
                _cursor.Y = y;
                if (_cursor.Y < 0) _cursor.Y = 0;
                if (_cursor.Y >= _vtSize.Y) _cursor.Y = _vtSize.Y - 1;
            } else {
                _cursor.X += x;
                _cursor.Y += y;
                if (_cursor.X < 0) _cursor.X = 0;
                if (_cursor.X >= _vtSize.X) { _cursor.X = 0; _cursor.Y++; }
                if (_cursor.Y < 0) _cursor.Y = 0;
                if (_cursor.Y >= _vtSize.Y) ScrollUp();
            }
            _dirty = true;
        }

        private Brush ANSIBrush(byte code, bool dim = false, bool bright = false)
        {
            switch (code)
            {
                case 30: // black
                case 40:
                    if (bright) return Brushes.DimGray;
                    return Brushes.Black;
                case 31: // red
                case 41:
                    if (bright) return Brushes.Red;
                    if (dim) return Brushes.Maroon;
                    return Brushes.Firebrick;
                case 32: // green
                case 42:
                    if (bright) return Brushes.Lime;
                    if (dim) return Brushes.DarkGreen;
                    return Brushes.Green;
                case 33: // yellow
                case 43:
                    if (bright) return Brushes.Yellow;
                    if (dim) return Brushes.DarkGoldenrod;
                    return Brushes.Goldenrod;
                case 34: // blue
                case 44:
                    if (bright) return Brushes.Blue;
                    if (dim) return Brushes.Navy;
                    return Brushes.MediumBlue;
                case 35: // magenta
                case 45:
                    if (bright) return Brushes.Magenta;
                    if (dim) return Brushes.Indigo;
                    return Brushes.DarkMagenta;
                case 36: // cyan
                case 46:
                    if (bright) return Brushes.Cyan;
                    if (dim) return Brushes.MediumTurquoise;
                    return Brushes.DeepSkyBlue;
                case 37: // white
                case 47:
                    if (bright) return Brushes.White;
                    if (dim) return Brushes.DarkGray;
                    return Brushes.LightGray;
                default:
                    return Brushes.Transparent;
            }
        }

        private void SetColor(Brush foregroundBrush = null, Brush backgroundBrush = null)
        {
            if (foregroundBrush != null)
            {
                _fgc = foregroundBrush;
                _fgcMap[_cursor.Y * _vtSize.X + _cursor.X] = _fgc;
            }
            if (backgroundBrush != null)
            {
                _bgc = backgroundBrush;
                _bgcMap[_cursor.Y * _vtSize.X + _cursor.X] = _bgc;
            }
            _dirty = true;
        }

        private void SetANSIColor(int? intensity = null, int? foreground = null, int? background = null)
        {
            Brush fg = null;
            Brush bg = null;
            if (intensity == 0)
            {
                if (foreground == null) foreground = 37;
                if (background == null) background = 40;
            }
            if (foreground != null)
            {
                fg = ANSIBrush(Convert.ToByte(foreground), intensity == 2, intensity == 1);
            }
            if (background != null)
            {
                bg = ANSIBrush(Convert.ToByte(background), intensity == 2, intensity == 1);
            }
            SetColor(fg, bg);
        }

        private void CursorOn()
        {
            if (_cursorState) return;
            _cursorState = true;
            var t = _fgcMap[_cursor.Y * _vtSize.X + _cursor.X];
            _fgcMap[_cursor.Y * _vtSize.X + _cursor.X] = _bgcMap[_cursor.Y * _vtSize.X + _cursor.X];
            _bgcMap[_cursor.Y * _vtSize.X + _cursor.X] = t;
            _dirty = true;
        }

        private void CursorOff()
        {
            if (!_cursorState) return;
            _cursorState = false;
            var t = _fgcMap[_cursor.Y * _vtSize.X + _cursor.X];
            _fgcMap[_cursor.Y * _vtSize.X + _cursor.X] = _bgcMap[_cursor.Y * _vtSize.X + _cursor.X];
            _bgcMap[_cursor.Y * _vtSize.X + _cursor.X] = t;
            _dirty = true;
        }

        private void Print(string str)
        {
            if (str != null)
            {
                for(var i = 0; i < str.Length; i++)
                {
                    Print(Convert.ToByte(str[i]));
                }
            }
        }

        private void Print(byte chr)
        {
            _cursorTimer.Stop();
            CursorOff();
            if (IsPrintable(chr))
            {
                _fgcMap[_cursor.Y * _vtSize.X + _cursor.X] = _fgc;
                _bgcMap[_cursor.Y * _vtSize.X + _cursor.X] = _bgc;
                _cells[_cursor.Y * _vtSize.X + _cursor.X] = chr;
                MoveCursor();
            } else
            {
                switch (chr)
                {
                    case 10:
                        MoveCursor(0, _cursor.Y, true);
                        break;
                    case 13:
                        MoveCursor(0, +1);
                        break;
                }
            }
            _cursorTimer.Start();
        }

        private void Digest(string str)
        {
            if (str != null)
            {
                for (var i = 0; i < str.Length; i++)
                {
                    Digest(Convert.ToByte(str[i]));
                }
            }
        }

        private void Digest(byte chr)
        {
            var escapeSequence = _escapeSequence != null;
            if (escapeSequence)
            {
                if (_escapeSequence.Length == 0 && chr != 91)
                {
                    escapeSequence = false;
                }
                else
                {
                    if (new byte[] { 65, 66, 67, 68, 72, 74, 75, 82, 98, 104, 108, 109, 110, 114, 116 }.Contains(chr))
                    { // end of escape sequence
                        _escapeSequence = _escapeSequence.Trim(new char[] { '[' });
                        var parameter = 0;
                        var parameters = _escapeSequence.Split(';');
                        switch (chr)
                        {
                            case 65:   // "A" - cursor up
                                parameter = Convert.ToInt32(_escapeSequence);
                                MoveCursor(0, -parameter, false);
                                break;
                            case 66:   // "B" - cursor down
                                parameter = Convert.ToInt32(_escapeSequence);
                                MoveCursor(0, parameter, false);
                                break;
                            case 67:   // "C" - cursor right
                                parameter = Convert.ToInt32(_escapeSequence);
                                MoveCursor(parameter, 0, false);
                                break;
                            case 68:   // "D" - cursor left
                                parameter = Convert.ToInt32(_escapeSequence);
                                MoveCursor(-parameter, 0, false);
                                break;
                            case 69:   // "E" - cursor down and home
                                parameter = Convert.ToInt32(_escapeSequence);
                                MoveCursor(-256, parameter, false);
                                break;
                            case 70:   // "F" - cursor up and home
                                parameter = Convert.ToInt32(_escapeSequence);
                                MoveCursor(-256, -parameter, false);
                                break;
                            case 71:   // "G" - move to column in current row
                                parameter = Convert.ToInt32(_escapeSequence);
                                MoveCursor(parameter, _cursor.Y, true);
                                break;
                            case 72:   // "H" - set cursor position
                                var x = 0;
                                var y = 0;
                                if (parameters.Length == 2)
                                {
                                    x = Convert.ToInt32(parameters[1]) - 1;
                                    y = Convert.ToInt32(parameters[0]) - 1;
                                }
                                MoveCursor(x, y, true);
                                break;
                            case 74:    // "J" - erase display
                                if (_escapeSequence.Length > 0) parameter = Convert.ToInt32(_escapeSequence);
                                switch (parameter)
                                {
                                    case 0: // from cursor to end of the screen
                                        for (var i = _cursor.Y * _vtSize.X + _cursor.X; i < _vtSize.X * _vtSize.Y - 1; i++)
                                        {
                                            _cells[i] = 32;
                                            _fgcMap[i] = Brushes.LightGray;// _fgc;
                                            _bgcMap[i] = Brushes.Black;// _bgc;
                                        }
                                        break;
                                    case 1: // from cursor to start of screen
                                        for(var i = _cursor.Y * _vtSize.X + _cursor.X; i >= 0; i++)
                                        {
                                            _cells[i] = 32;
                                            _fgcMap[i] = Brushes.LightGray;// _fgc;
                                            _bgcMap[i] = Brushes.Black;// _bgc;
                                        }
                                        break;
                                    default: // clear entire screen
                                        Reset();
                                        break;
                                }
                                break;
                            case 75:    // "K" - erase line
                                if (_escapeSequence.Length > 0) parameter = Convert.ToInt32(_escapeSequence);
                                switch (parameter)
                                {
                                    case 0: // from cursor to end of line
                                        for (var i = _cursor.X; i < _vtSize.X; i++)
                                        {
                                            _cells[_cursor.Y * _vtSize.X + i] = 32;
                                            _fgcMap[_cursor.Y * _vtSize.X + i] = Brushes.LightGray;// _fgc;
                                            _bgcMap[_cursor.Y * _vtSize.X + i] = Brushes.Black;// _bgc;
                                        }
                                        break;
                                    case 1: // from cursor to start of line
                                        for (var i = 0; i < _cursor.X; i++)
                                        {
                                            _cells[_cursor.Y * _vtSize.X + i] = 32;
                                            _fgcMap[_cursor.Y * _vtSize.X + i] = Brushes.LightGray;// _fgc;
                                            _bgcMap[_cursor.Y * _vtSize.X + i] = Brushes.Black;// _bgc;
                                        }
                                        break;
                                    default: // clear entire line
                                        for (var i = 0; i < _vtSize.X; i++)
                                        {
                                            _cells[_cursor.Y * _vtSize.X + i] = 32;
                                            _fgcMap[_cursor.Y * _vtSize.X + i] = Brushes.LightGray;// _fgc;
                                            _bgcMap[_cursor.Y * _vtSize.X + i] = Brushes.Black;// _bgc;
                                        }
                                        break;
                                }
                                break;
                            case 98:
                                break;
                            case 104:   // "h" - set mode
                                try { parameter = Convert.ToInt32(_escapeSequence); } catch (Exception) { }
                                switch (parameter)
                                {
                                    case 25: // make cursor visible
                                        _cursorEnabled = true;
                                        break;
                                }
                                break;
                            case 108:   // "l" - reset mode
                                try { parameter = Convert.ToInt32(_escapeSequence); } catch (Exception) { }
                                switch (parameter)
                                {
                                    case 25: // make cursor visible
                                        break;
                                }
                                Reset();
                                break;
                            case 110:   // "n" - status report
                                parameter = Convert.ToInt32(_escapeSequence);
                                switch (parameter)
                                {
                                    case 5: // device status
                                        break;
                                    case 6: // current cursor position
                                        var response = new List<byte> { 27, 91 };
                                        response.AddRange(Encoding.ASCII.GetBytes(_cursor.X.ToString()));
                                        response.Add(44);
                                        response.AddRange(Encoding.ASCII.GetBytes(_cursor.Y.ToString()));
                                        response.Add(82);
                                        if (_ansi) Send(response.ToArray());
                                        break;
                                }
                                break;
                            case 109:   // "m" - color command
                                int? bg = null;
                                int? fg = null;
                                int? intensity = null;
                                if (parameters.Length == 1)
                                {
                                    var attr = Convert.ToInt32(parameters[0]);
                                    if (attr < 10) { intensity = attr; }
                                    else if (attr < 40) fg = attr;
                                    else bg = attr;
                                }
                                else
                                {
                                    for (var i = 0; i < parameters.Length; i++)
                                    {
                                        var attr = Convert.ToInt32(parameters[i]);
                                        if (attr == 0)
                                        {
                                            SetANSIColor(0);
                                        }
                                        else
                                        {
                                            if (attr < 10) {
                                                intensity = attr;
                                            }
                                            else
                                            {
                                                if (attr < 40) fg = attr;
                                                else bg = attr;
                                            }
                                        }
                                    }
                                }
                                SetANSIColor(intensity, fg, bg);
                                break;
                            default:
                                break;
                        }
                        _escapeSequence = null;
                    }
                    else
                    {
                        _escapeSequence += (char)chr;
                    }
                }
            }
            if (!escapeSequence) {
                switch (chr)
                {
                    case 0:
                        return;
                    case 7:
                        SystemSounds.Beep.Play();
                        return;
                    case 27:    // escape
                        if (_socket != null && _socket.Connected) // looks like we're getting an escape sequence from socket
                        {
                            _escapeSequence = "";
                        }
                        break;
                    default:
                        Print(chr);
                        break;
                }
            }
        }

        private void Send(byte[] data)
        {
            try
            {
                if (_socket != null && _socket.Connected) { _socket.Send(data); }
            }
            catch (Exception e)
            {
                _listening = false;
                _socket = null;
                Print(13);
                Print(10);
                SetANSIColor(0);
                Reset();
                Print(e.Message + (char)13 + (char)10);
            }
        }

        private void Send(byte chr)
        {
            Send(new byte[] { chr });
        }

        private void UpdateScreen()
        {
            if (_drawing) return; // Just in case this method is being invoked while it is stil working.
            _drawing = true;
            _dirty = false;
            try
            {
                _font = new Font(pfc.Families[0], 32, FontStyle.Regular, GraphicsUnit.Pixel);
                for (var y = _vtSize.Y - 1; y >= 0; y--)
                {
                    if (!_drawing) break;
                    for (var x = 0; x < _vtSize.X; x++)
                    {
                        if (!_drawing) break;
                        if (_cells[y * _vtSize.X + x] != _buffer[y * _vtSize.X + x]
                         || _fgcMap[y * _vtSize.X + x] != _fgcBuffer[y * _vtSize.X + x]
                         || _bgcMap[y * _vtSize.X + x] != _bgcBuffer[y * _vtSize.X + x]
                         || (x == _cursor.X && y == _cursor.Y))
                        {
                            _buffer[y * _vtSize.X + x] = _cells[y * _vtSize.X + x];
                            _fgcBuffer[y * _vtSize.X + x] = _fgcMap[y * _vtSize.X + x];
                            _bgcBuffer[y * _vtSize.X + x] = _bgcMap[y * _vtSize.X + x];
                            _screen.FillRectangle(_bgcMap[y * _vtSize.X + x], x * _char.X - 1, y * _char.Y, _char.X + 1, _char.Y);
                            _screen.DrawString(ConvertToChar(_cells[y * _vtSize.X + x]).ToString(), _font, _fgcMap[y * _vtSize.X + x], new Rectangle(_char.X * x, _char.Y * y, _char.X, _char.Y), StringFormat.GenericTypographic);
                        }
                    }
                }
                if (_usePictureBox) 
                    canvas.Invalidate();
                else 
                    _graphics.DrawImage(_bitmap, 0, 0, ClientSize.Width, ClientSize.Height);
            }
            catch (Exception) { } // We would like not to throw an exception when _screen is already disposed upon form closing, while we were drawing stuff...
            _drawing = false;
        }

        private void GraphicsThread()
        {
            while (this.Visible)
            {
                if (!_drawing && _dirty) UpdateScreen();
                Thread.Sleep(1);
            }
        }

        private static Socket ConnectSocket(string server, int port)
        {
            Socket s = null;
            IPHostEntry hostEntry = null;
            hostEntry = Dns.GetHostEntry(server);
            foreach (IPAddress address in hostEntry.AddressList)
            {
                IPEndPoint ipe = new IPEndPoint(address, port);
                Socket tempSocket = new Socket(ipe.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                tempSocket.Connect(ipe);
                if (tempSocket.Connected)
                {
                    s = tempSocket;
                    break;
                }
                else
                {
                    continue;
                }
            }
            return s;
        }

        private void Listen()
        {
            while (_listening && _socket != null && _socket.Connected)
            {
                if (_socket != null && _socket.Connected && _socket.Available > 0 && !_drawing)
                {
                    Byte[] bytesReceived = new Byte[32];
                    int bytes = _socket.Receive(bytesReceived, 32, 0);
                    for (var i = 0; i < bytes; i++)
                    {
                        Digest(bytesReceived[i]);
                    }
                }
            }
        }

        private void Connect(string address, int port)
        {
            _connecting = true;
            Print($"Connecting to {address}:{port}" + (char)13 + (char)10);
            try
            {
                _socket = ConnectSocket(address, port);
                if (_socket == null)
                {
                    throw new Exception("Connection failed.");
                }
                Print("Connected." + (char)13 + (char)10);
                _listening = true;
            }
            catch (Exception e)
            {
                Print(e.Message + (char)13 + (char)10);
            }
            _socketThread = new Thread(Listen);
            _socketThread.Start();
            _connecting = false;
        }

        private void Command()
        {
            var str = GetLine(_cursor.Y - 1).TrimEnd();
            if (String.IsNullOrEmpty(str)) return;
            MoveCursor(0, _cursor.Y, true);
            var cmd = str.Trim().Split(' ');
            if (cmd[0].ToLower() != "?" && cmd[0].ToLower() != "help") _lastCommand = str;
            switch (cmd[0].ToLower())
            {
                case "ansi":
                    _ansi = true;
                    Print("Terminal mode is " + (_ansi ? "ANSI" : "ASCII") + (char)13 + (char)10);
                    break;
                case "ascii":
                    _ansi = false;
                    Print("Terminal mode is " + (_ansi ? "ANSI" : "ASCII") + (char)13 + (char)10);
                    break;
                case "connect":
                case "open":
                    switch(cmd.Length){
                        case 2:
                            Connect(cmd[1].ToLower().Trim(), 23);
                            break;
                        case 3:
                            Connect(cmd[1].ToLower().Trim(), Convert.ToInt32(cmd[2]));
                            break;
                        default:
                            Print("open HOSTNAME|ADDRESS [PORT]" + (char)13 + (char)10);
                            break;
                    }
                    break;
                case "crlf":
                    if (cmd.Length == 2)
                    {
                        switch (cmd[1].ToLower().Trim())
                        {
                            case "1":
                            case "on":
                                _crLf = true;
                                break;
                            case "0":
                            case "off":
                                _crLf = false;
                                break;
                        }
                    }
                    else
                    {
                        _crLf = !_crLf;
                    }
                    Print("CR+LF is " + (_crLf ? "ON" : "OFF") + (char)13 + (char)10);
                    break;
                case "cur":
                case "cursor":
                    if (cmd.Length == 2)
                    {
                        switch (cmd[1].ToLower().Trim())
                        {
                            case "1":
                            case "on":
                                _cursorEnabled = true;
                                break;
                            case "0":
                            case "off":
                                _cursorEnabled = false;
                                break;
                        }
                    }
                    else
                    {
                        _cursorEnabled = !_cursorEnabled;
                    }
                    Print("Cursor is " + (_cursorEnabled ? "ON" : "OFF") + (char)13 + (char)10);
                    break;
                case "echo":
                    if (cmd.Length == 2)
                    {
                        switch (cmd[1].ToLower().Trim())
                        {
                            case "1":
                            case "on":
                                _localEcho = true;
                                break;
                            case "0":
                            case "off":
                                _localEcho = false;
                                break;
                        }
                    }
                    else
                    {
                        _localEcho = !_localEcho;
                    }
                    Print("Local echo is " + (_localEcho ? "ON" : "OFF") + (char)13 + (char)10);
                    break;
                case "exit":
                case "quit":
                    Close();
                    break;
                case "help":
                    Print("=== Accepted commands are: ===" + (char)13 + (char)10);
                    Print("ANSI, ASCII, CONNECT, CRLF, CUR, CURSOR, ECHO, EXIT, HELP, OPEN, STATUS, QUIT, ?" + (char)13 + (char)13 + (char)10);
                    break;
                case "status":
                case "?":
                    Print("=== Current connection settings: ===" + (char)13 + (char)10);
                    Print("Terminal mode is " + (_ansi ? "ANSI" : "ASCII") + (char)13 + (char)10);
                    Print("        CR+LF is " + (_crLf ? "ON" : "OFF") + (char)13 + (char)10);
                    Print("       Cursor is " + (_cursorEnabled ? "ON" : "OFF") + (char)13 + (char)10);
                    Print("   Local echo is " + (_localEcho ? "ON" : "OFF") + (char)13 + (char)13 + (char)10);
                    break;
                case "":
                    break;
                default:
                    Print("Command unrecognized." + (char)13 + (char)10);
                    break;
            }
        }

        private void OnCursorTimer(object sender, ElapsedEventArgs e)
        {
            if (((_socket != null && _socket.Connected) && !_cursorEnabled)) return;
            if (_cursorState) CursorOff(); else CursorOn();
            _dirty = true;
        }

        private void OnShow(object sender, EventArgs e)
        {
            _socketThread = new Thread(GraphicsThread);
            _socketThread.Start();
            Print("Terminal ready." + (char)13 + (char)10);
            UpdateScreen();
            if (!String.IsNullOrEmpty(_connectHost))
            {
                Connect(_connectHost, _connectPort);
            }
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            
            _drawing = false;
            _listening = false;
            if (_socket != null && _socket.Connected) _socket.Close();
            //_refreshTimer.Dispose();
            _cursorTimer.Dispose();
            _screen.Dispose();
            _bitmap.Dispose();
        }

        private void OnPreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            if (e.KeyCode != Keys.Alt) e.IsInputKey = true;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (_socket != null)
            {
                //if (_ansi && e.KeyCode == Keys.PageUp)      Send(new byte[] { 27, 91, 49, 48, 65 });
                //if (_ansi && e.KeyCode == Keys.PageDown)    Send(new byte[] { 27, 91, 49, 48, 66 });
                //if (_ansi && e.KeyCode == Keys.End)         Send(new byte[] { 27, 91, 56, 48, 67 });
                //if (_ansi && e.KeyCode == Keys.Home)        Send(new byte[] { 27, 91, 56, 48, 68 });
                if (_ansi && e.KeyCode == Keys.Left)        Send(new byte[] { 27, 91, 68 });
                if (_ansi && e.KeyCode == Keys.Up)          Send(new byte[] { 27, 91, 65 });
                if (_ansi && e.KeyCode == Keys.Right)       Send(new byte[] { 27, 91, 67 });
                if (_ansi && e.KeyCode == Keys.Down)        Send(new byte[] { 27, 91, 66 });
                //if (e.Alt && e.KeyCode == Keys.X)           Disconnect(); // Planned for future.
            }
            else
            {
                if (_connecting) return;
                if (e.KeyCode == Keys.Back)     { MoveCursor(-1); _cells[_cursor.Y * _vtSize.X + _cursor.X] = 32; }
                if (e.KeyCode == Keys.Escape)   { SetLine("".PadRight(_vtSize.X)); MoveCursor(0, _cursor.Y, true); }
                if (e.KeyCode == Keys.Up)       { var str = GetLine().TrimEnd(); if (str == "" && _lastCommand != null) Print(_lastCommand); }
                if (e.KeyCode == Keys.Left)     { MoveCursor(-1); }
                if (e.KeyCode == Keys.Right)    { if (_cursor.X < GetLine().TrimEnd().Length) MoveCursor(1); }
            }
        }

        private void OnKeyPress(object sender, KeyPressEventArgs e)
        {
            if (_socket != null && _socket.Connected)
            {
                if (_localEcho)
                {
                    Digest((byte)e.KeyChar);
                    if (e.KeyChar == 13 && _crLf) Digest(10);
                }
                Send((byte)e.KeyChar);
            }
            else
            {
                if (_connecting) return;
                Digest((byte)e.KeyChar);
                if (e.KeyChar == 13)
                {
                    Digest(10);
                    Command();
                }
            }
        }

        private void OnResize(object sender, EventArgs e)
        {
            if (!_usePictureBox) _graphics = CreateGraphics();
            if (!_drawing) UpdateScreen();
        }
    }
}
