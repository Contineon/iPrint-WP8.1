using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;
#if WINDOWS_PHONE_APP
using Windows.Phone.UI.Input;
#endif

namespace iprint
{
#if WINDOWS_PHONE_APP
    /// <summary>Pages that start a FileOpenPicker "AndContinue" operation implement this.</summary>
    public interface IFileOpenPickerContinuable
    {
        void ContinueFileOpenPicker(FileOpenPickerContinuationEventArgs args);
    }
#endif

    public sealed partial class App : Application
    {
#if WINDOWS_PHONE_APP
        private TransitionCollection transitions;

        /// <summary>Set by a page while back navigation must be blocked (printing).</summary>
        public static bool BlockBack;

        /// <summary>Picker result that arrived while the target page was not on screen.</summary>
        public static FileOpenPickerContinuationEventArgs PendingPick;
#endif

        public App()
        {
            this.InitializeComponent();
            this.Suspending += this.OnSuspending;
#if WINDOWS_PHONE_APP
            HardwareButtons.BackPressed += OnBackPressed;
#endif
        }

#if WINDOWS_PHONE_APP
        void OnBackPressed(object sender, BackPressedEventArgs e)
        {
            var frame = Window.Current.Content as Frame;
            if (frame == null) return;
            if (BlockBack) { e.Handled = true; return; }
            if (frame.CanGoBack)
            {
                e.Handled = true;
                frame.GoBack();
            }
        }
#endif

        Frame EnsureFrame()
        {
            Frame rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.CacheSize = 1;
                Window.Current.Content = rootFrame;
            }
            return rootFrame;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            Frame rootFrame = EnsureFrame();

            if (rootFrame.Content == null)
            {
#if WINDOWS_PHONE_APP
                if (rootFrame.ContentTransitions != null)
                {
                    this.transitions = new TransitionCollection();
                    foreach (var c in rootFrame.ContentTransitions)
                    {
                        this.transitions.Add(c);
                    }
                }

                rootFrame.ContentTransitions = null;
                rootFrame.Navigated += this.RootFrame_FirstNavigated;
#endif
                if (!rootFrame.Navigate(typeof(MainPage), e.Arguments))
                {
                    throw new Exception("Failed to create initial page");
                }
            }

            Window.Current.Activate();
        }

#if WINDOWS_PHONE_APP
        protected override void OnActivated(IActivatedEventArgs args)
        {
            base.OnActivated(args);
            var pick = args as FileOpenPickerContinuationEventArgs;
            if (pick == null) return;

            Frame rootFrame = EnsureFrame();
            var page = rootFrame.Content as IFileOpenPickerContinuable;
            if (page != null)
            {
                page.ContinueFileOpenPicker(pick);
            }
            else
            {
                // App was terminated while the picker was open: rebuild the navigation stack.
                PendingPick = pick;
                object target;
                pick.ContinuationData.TryGetValue("target", out target);
                if (rootFrame.Content == null) rootFrame.Navigate(typeof(MainPage));
                rootFrame.Navigate("doc".Equals(target) ? typeof(DocumentPreviewPage) : typeof(PhotoPreviewPage));
            }
            Window.Current.Activate();
        }

        private void RootFrame_FirstNavigated(object sender, NavigationEventArgs e)
        {
            var rootFrame = sender as Frame;
            rootFrame.ContentTransitions = this.transitions ?? new TransitionCollection() { new NavigationThemeTransition() };
            rootFrame.Navigated -= this.RootFrame_FirstNavigated;
        }
#endif

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            deferral.Complete();
        }
    }
}
