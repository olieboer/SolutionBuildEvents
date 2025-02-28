using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json;
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using Task = System.Threading.Tasks.Task;

namespace SolutionBuildEvents
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class EnableSolutionBuildEventsCommand
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EnableSolutionBuildEventsCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        public EnableSolutionBuildEventsCommand(AsyncPackage package)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in EnableSolutionBuildEventsCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);

            _dte = (DTE2)await package.GetServiceAsync(typeof(DTE));
            DetermineConfigPath();

            ConfigurationChangeListener listener = new ConfigurationChangeListener(package);

            _dte.Events.SolutionEvents.Opened += DetermineConfigPath;
            _dte.Events.BuildEvents.OnBuildBegin += async (scope, action) => await HandleBuildEventAsync(EEventType.PreBuild).ConfigureAwait(false);
            _dte.Events.BuildEvents.OnBuildDone += async (scope, action) => await HandleBuildEventAsync(EEventType.PostBuild).ConfigureAwait(false);
            
            listener.ConfigChanged += async config => HandleBuildEventAsync(EEventType.ConfigurationChanged);
        }

        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("0d8e3d67-f680-4b04-8acc-94e4eb2f34a3");

        public static readonly Guid SolutionBuildEventOutputPaneGuid = new Guid("19D6E89E-246D-46A9-87B9-5C7B90F1EB31");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;
        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private IAsyncServiceProvider ServiceProvider => package;
        private DTE2 _dte;

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static EnableSolutionBuildEventsCommand Instance { get; private set; }

        private string _configFilePath;
        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private async void Execute(object sender, EventArgs e)
        {
            DetermineConfigPath();
            if (!File.Exists(_configFilePath))
            {
                Parameter p = new Parameter
                {
                    PreBuildEvent = new[] { "rem PrebuildEvent" },
                    PostBuildEvent = new[] { "rem PostbuildEvent" },
                    ConfigurationChangedEvent = new[] { "rem ConfigurationChangedEvent" }
                };

                var s = JsonConvert.SerializeObject(p, Formatting.Indented);
                File.WriteAllText(_configFilePath, s);
            }
            System.Diagnostics.Process.Start(_configFilePath);
        }

        private async void DetermineConfigPath()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            
            _configFilePath = Path.Combine(Path.GetDirectoryName(_dte.Solution.FullName), "SolutionBuildEvents.json");
        }

        private enum EEventType
        {
            PreBuild,
            PostBuild,
            ConfigurationChanged
        }

        private async Task HandleBuildEventAsync(EEventType eventType)
        {
            if (!File.Exists(_configFilePath))
            {
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            string jsonText = File.ReadAllText(_configFilePath);

            Parameter p = JsonConvert.DeserializeObject<Parameter>(jsonText);
            switch (eventType)
            {
                case EEventType.PreBuild:
                    await ExecuteCommandAsync(p.PreBuildEvent, "Prebuild event");
                    break;
                case EEventType.PostBuild:
                    await ExecuteCommandAsync(p.PostBuildEvent, "Postbuild event");
                    break;
                case EEventType.ConfigurationChanged:
                    await ExecuteCommandAsync(p.ConfigurationChangedEvent, "ConfigurationChanged event");
                    break;
            }
        }

        private async Task ExecuteCommandAsync(string[] commands, string header)
        {
            foreach (string line in commands)
            {
                var processStartInfo = new ProcessStartInfo("cmd.exe", $"/c {line}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                var process = new System.Diagnostics.Process
                {
                    StartInfo = processStartInfo,
                    EnableRaisingEvents = true
                };

                process.OutputDataReceived += (sender, args) => WriteToOutputWindow($"{header}", args.Data);
                process.ErrorDataReceived += (sender, args) => WriteToOutputWindow($"{header} (ERROR)", args.Data);

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();
            }
        }

        private void WriteToOutputWindow(string header, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (!(Package.GetGlobalService(typeof(SVsOutputWindow)) is IVsOutputWindow outputWindow))
                {
                    return Task.CompletedTask;
                }

                outputWindow.CreatePane(SolutionBuildEventOutputPaneGuid, "Solution Build Events", 1, 1);
                outputWindow.GetPane(SolutionBuildEventOutputPaneGuid, out var pane);
                pane.OutputString($"{header}: {message}{Environment.NewLine}");
                return Task.CompletedTask;
            });

        }
    }
}
