using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Xilium.CefGlue;
using Xilium.CefGlue.Wrapper;

namespace TestRender {
    class DemoRenderProcessHandler : CefRenderProcessHandler {
        public static bool DumpProcessMessages { get; set; }

        public DemoRenderProcessHandler() {
            MessageRouter = new CefMessageRouterRendererSide(new CefMessageRouterConfig());
        }

        internal CefMessageRouterRendererSide MessageRouter { get; private set; }

        protected override void OnContextCreated(CefBrowser browser, CefFrame frame, CefV8Context context) {
            MessageRouter.OnContextCreated(browser, frame, context);
        }

        protected override void OnContextReleased(CefBrowser browser, CefFrame frame, CefV8Context context) {
            MessageRouter.OnContextReleased(browser, frame, context);
        }

        protected override bool OnProcessMessageReceived(CefBrowser browser, CefProcessId sourceProcess, CefProcessMessage message) {
            if(DumpProcessMessages) {
                Console.WriteLine("Render::OnProcessMessageReceived: SourceProcess={0}", sourceProcess);
                Console.WriteLine("Message Name={0} IsValid={1} IsReadOnly={2}", message.Name, message.IsValid, message.IsReadOnly);
                var arguments = message.Arguments;
                for(var i = 0; i < arguments.Count; i++) {
                    var type = arguments.GetValueType(i);
                    object value;
                    switch(type) {
                        case CefValueType.Null: value = null; break;
                        case CefValueType.String: value = arguments.GetString(i); break;
                        case CefValueType.Int: value = arguments.GetInt(i); break;
                        case CefValueType.Double: value = arguments.GetDouble(i); break;
                        case CefValueType.Bool: value = arguments.GetBool(i); break;
                        default: value = null; break;
                    }

                    Console.WriteLine("  [{0}] ({1}) = {2}", i, type, value);
                }
            }

            var handled = MessageRouter.OnProcessMessageReceived(browser, sourceProcess, message);
            if(handled) return true;

            if(message.Name == "myMessage2") return true;

            var message2 = CefProcessMessage.Create("myMessage2");
            var success = browser.SendProcessMessage(CefProcessId.Renderer, message2);
            Console.WriteLine("Sending myMessage2 to renderer process = {0}", success);

            var message3 = CefProcessMessage.Create("myMessage3");
            var success2 = browser.SendProcessMessage(CefProcessId.Browser, message3);
            Console.WriteLine("Sending myMessage3 to browser process = {0}", success);

            return false;
        }
    }

    internal sealed class DemoBrowserProcessHandler : CefBrowserProcessHandler {
        protected override void OnBeforeChildProcessLaunch(CefCommandLine commandLine) {
            Console.WriteLine("AppendExtraCommandLineSwitches: {0}", commandLine);
            Console.WriteLine(" Program == {0}", commandLine.GetProgram());

            // .NET in Windows treat assemblies as native images, so no any magic required.
            // Mono on any platform usually located far away from entry assembly, so we want prepare command line to call it correctly.
            if(Type.GetType("Mono.Runtime") != null) {
                if(!commandLine.HasSwitch("cefglue")) {
                    var path = new Uri(Assembly.GetEntryAssembly().CodeBase).LocalPath;
                    commandLine.SetProgram(path);

                    var mono = CefRuntime.Platform == CefRuntimePlatform.Linux ? "/usr/bin/mono" : @"C:\Program Files\Mono-2.10.8\bin\monow.exe";
                    commandLine.PrependArgument(mono);

                    commandLine.AppendSwitch("cefglue", "w");
                }
            }

            Console.WriteLine("  -> {0}", commandLine);
        }
    }

    public class TestApp : CefApp {
        private CefBrowserProcessHandler _browserProcessHandler = new DemoBrowserProcessHandler();
        private CefRenderProcessHandler _renderProcessHandler = new DemoRenderProcessHandler();

        protected override void OnBeforeCommandLineProcessing(string processType, CefCommandLine commandLine) {
            Console.WriteLine("OnBeforeCommandLineProcessing: {0} {1}", processType, commandLine);

            // TODO: currently on linux platform location of locales and pack files are determined
            // incorrectly (relative to main module instead of libcef.so module).
            // Once issue http://code.google.com/p/chromiumembedded/issues/detail?id=668 will be resolved
            // this code can be removed.
            if(CefRuntime.Platform == CefRuntimePlatform.Linux) {
                var path = new Uri(Assembly.GetEntryAssembly().CodeBase).LocalPath;
                path = Path.GetDirectoryName(path);

                commandLine.AppendSwitch("resources-dir-path", path);
                commandLine.AppendSwitch("locales-dir-path", Path.Combine(path, "locales"));
            }
        }

        protected override CefBrowserProcessHandler GetBrowserProcessHandler() {
            return _browserProcessHandler;
        }

        protected override CefRenderProcessHandler GetRenderProcessHandler() {
            return _renderProcessHandler;
        }
    }

    internal class DemoCefLoadHandler : CefLoadHandler {
        protected override void OnLoadStart(CefBrowser browser, CefFrame frame) {
            // A single CefBrowser instance can handle multiple requests
            //   for a single URL if there are frames (i.e. <FRAME>, <IFRAME>).
            if(frame.IsMain) {
                Console.WriteLine("START: {0}", browser.GetMainFrame().Url);
            }
        }

        protected override void OnLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode) {
            if(frame.IsMain) {
                Console.WriteLine("END: {0}, {1}", browser.GetMainFrame().Url, httpStatusCode);
            }
        }
    }

    internal class DemoCefRenderHandler : CefRenderHandler {
        private readonly int _windowHeight;
        private readonly int _windowWidth;

        public DemoCefRenderHandler(int windowWidth, int windowHeight) {
            _windowWidth = windowWidth;
            _windowHeight = windowHeight;
        }

        protected override bool GetRootScreenRect(CefBrowser browser, ref CefRectangle rect) {
            return GetViewRect(browser, ref rect);
        }

        protected override bool GetScreenPoint(CefBrowser browser, int viewX, int viewY, ref int screenX, ref int screenY) {
            screenX = viewX;
            screenY = viewY;
            return true;
        }

        protected override bool GetViewRect(CefBrowser browser, ref CefRectangle rect) {
            rect.X = 0;
            rect.Y = 0;
            rect.Width = _windowWidth;
            rect.Height = _windowHeight;
            return true;
        }

        protected override bool GetScreenInfo(CefBrowser browser, CefScreenInfo screenInfo) {
            return false;
        }

        protected override void OnPopupSize(CefBrowser browser, CefRectangle rect) {
        }

        protected override void OnPaint(CefBrowser browser, CefPaintElementType type, CefRectangle[] dirtyRects, IntPtr buffer, int width, int height) {
            // Save the provided buffer (a bitmap image) as a PNG.
            var bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppRgb, buffer);
            bitmap.Save(@"c:\temp\LastOnPaint.png", ImageFormat.Png);
        }

        protected override void OnCursorChange(CefBrowser browser, IntPtr cursorHandle, CefCursorType type, CefCursorInfo customCursorInfo) {  }
        protected override void OnScrollOffsetChanged(CefBrowser browser, double x, double y) {  }

        //protected override void OnCursorChange(CefBrowser browser, IntPtr cursorHandle) {
        //}

        //protected override void OnScrollOffsetChanged(CefBrowser browser) {
        //}
    }

    internal class DemoCefRequestHandler : CefRequestHandler {
        protected override unsafe CefReturnValue OnBeforeResourceLoad(CefBrowser browser, CefFrame frame, CefRequest request, CefRequestCallback callback) {
            string svg = "";
            using(var file = File.OpenText(@"c:\temp\sportscar.svg")) svg = file.ReadToEnd();

            frame.LoadString(svg, "dummy_url");

            return CefReturnValue.Cancel;
        }
    }

    internal class DemoCefClient : CefClient {
        private readonly DemoCefLoadHandler _loadHandler;
        private readonly DemoCefRenderHandler _renderHandler;
        //private readonly CefRequestHandler _requestHandler;

        public DemoCefClient(int windowWidth, int windowHeight) {
            _renderHandler = new DemoCefRenderHandler(windowWidth, windowHeight);
            _loadHandler = new DemoCefLoadHandler();
            //_requestHandler = new DemoCefRequestHandler();
        }

        protected override CefRenderHandler GetRenderHandler() {
            return _renderHandler;
        }

        protected override CefLoadHandler GetLoadHandler() {
            return _loadHandler;
        }

        //protected override unsafe CefRequestHandler GetRequestHandler() {
        //    return _requestHandler;
        //}
    }


    static class Program {
       
        [STAThread]
        static void Main(string[] args) {
            // Load CEF. This checks for the correct CEF version.
            CefRuntime.Load();

            // Start the secondary CEF process.
            var cefMainArgs = new CefMainArgs(new string[0]);
            var cefApp = new TestApp();

            // This is where the code path divereges for child processes.
            if(CefRuntime.ExecuteProcess(cefMainArgs, cefApp) != -1) {
                Console.Error.WriteLine("Could not the secondary process.");
            }

            // Settings for all of CEF (e.g. process management and control).
            var cefSettings = new CefSettings {
                SingleProcess = false,
                MultiThreadedMessageLoop = true
            };

            // Start the browser process (a child process).
            CefRuntime.Initialize(cefMainArgs, cefSettings, cefApp);

            // Instruct CEF to not render to a window at all.
            CefWindowInfo cefWindowInfo = CefWindowInfo.Create();
            cefWindowInfo.SetAsWindowless(IntPtr.Zero, true);

            // Settings for the browser window itself (e.g. enable JavaScript?).
            var cefBrowserSettings = new CefBrowserSettings();


            string url = "about:blank";
            int width = 1280, height = 768;
            if(args.Length > 0) url = args[0];
            if(args.Length > 1) width = int.Parse(args[1]);
            if(args.Length > 2) height = int.Parse(args[2]);

            // Initialize some the cust interactions with the browser process.
            // The browser window will be 1280 x 720 (pixels).
            var cefClient = new DemoCefClient(width, height);

            // Start up the browser instance.
            CefBrowserHost.CreateBrowser(
                cefWindowInfo,
                cefClient,
                cefBrowserSettings, url);

            //var frame = browser.GetMainFrame();
            //string svg = "";
            //using(var file = File.OpenText(@"c:\temp\sportscar.svg")) svg = file.ReadToEnd();

            //frame.LoadString(svg, "dummy_url");
            

            // Hang, to let the browser to do its work.
            Console.WriteLine("Press a key at any time to end the program.");
            Console.ReadKey();

            // Clean up CEF.
            CefRuntime.Shutdown();
        }
    }
}
