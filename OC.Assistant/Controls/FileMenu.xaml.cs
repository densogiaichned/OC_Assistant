﻿using System.IO;
using System.IO.Compression;
using System.Windows;
using Microsoft.Win32;
using OC.Assistant.Core;
using OC.Assistant.Core.TwinCat;
using OC.Assistant.Sdk;

namespace OC.Assistant.Controls;

internal partial class FileMenu : IProjectSelector
{
    private bool _solutionIsOpen;
    private EnvDTE.SolutionEvents? _solutionEvents;
    
    private static event Action<TcDte>? OnConnectSolution;
    private static event Action? OnOpenSolution;
    private static event Action? OnCreateSolution;
    
    public event Action<TcDte>? DteSelected;
    public event Action<string>? XmlSelected;
    public event Action? DteClosed;
    
    private void SolutionEventsOnAfterClosing()
    {
        if (_solutionEvents is not null)
        {
            _solutionEvents.AfterClosing -= SolutionEventsOnAfterClosing;
            _solutionEvents = null;
        }
        
        ComException.Raised -= SolutionEventsOnAfterClosing;
        DteClosed?.Invoke();
    }
    
    public FileMenu()
    {
        InitializeComponent();
        OnConnectSolution += SelectDte;
        OnOpenSolution += () => OpenSlnOnClick();
        OnCreateSolution += () => CreateSlnOnClick();
        ProjectManager.Instance.Subscribe(this);
    }
    
    public static void ConnectSolution(TcDte dte)
    {
        OnConnectSolution?.Invoke(dte);
    }
    
    public static void OpenSolution()
    {
        OnOpenSolution?.Invoke();
    }
    
    public static void CreateSolution()
    {
        OnCreateSolution?.Invoke();
    }

    private async void FileMenuOnLoaded(object sender, RoutedEventArgs e)
    {
        DteSelector.Selected += SelectDte;
        await InitializeDte();
        InitializeXml();
    }

    private void ExitOnClick(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private async Task InitializeDte()
    {
        if (!File.Exists(AppData.PreselectedProject)) return;
        var path = await File.ReadAllTextAsync(AppData.PreselectedProject);
        if (!Path.GetExtension(path).Equals(".sln", StringComparison.CurrentCultureIgnoreCase)) return;
        File.Delete(AppData.PreselectedProject);
        
        BusyState.Set(this);
        await GetSolutionFromPath(path);
        BusyState.Reset(this);
    }
    
    private void InitializeXml()
    {
        if (!File.Exists(AppData.PreselectedProject)) return;
        var path = File.ReadAllText(AppData.PreselectedProject);
        File.Delete(AppData.PreselectedProject);
        if (!Path.GetExtension(path).Equals(".xml", StringComparison.CurrentCultureIgnoreCase)) return;
        XmlSelected?.Invoke(path);
    }

    private async Task GetSolutionFromPath(string path)
    {
        await Task.Run(() =>
        {
            var selection = TcDte.GetInstances().FirstOrDefault(x => x.SolutionFullName == path);
            if (selection == default)
            {
                Logger.LogError(this, $"There is no open solution {path}.");
                return;
            }
            SelectDte(selection);
        });
    }
    
    private void SelectDte(TcDte dte)
    {
        _solutionEvents = dte.SolutionEvents;
        if (_solutionEvents is not null)
        {
            _solutionEvents.AfterClosing += SolutionEventsOnAfterClosing;
            ComException.Raised += SolutionEventsOnAfterClosing;
        }
        
        Logger.LogInfo(this, dte.SolutionFullName + " connected");

        //Show the shell
        if (!dte.UserControl) dte.UserControl = true;

        DteSelected?.Invoke(dte);
    }
    
    private async void OpenSlnOnClick(object? sender = null, RoutedEventArgs? e = null)
    {
        BusyState.Set(this);

        var openFileDialog = new OpenFileDialog
        {
            Filter = "TwinCAT Solution (*.sln)|*.sln",
            RestoreDirectory = true
        };
        
        if (openFileDialog.ShowDialog() == true)
        {
            await OpenDte(openFileDialog.FileName);
        }
        
        BusyState.Reset(this);
    }
    
    private async void CreateSlnOnClick(object? sender = null, RoutedEventArgs? e = null)
    {
        BusyState.Set(this);

        var saveFileDialog = new SaveFileDialog
        {
            Filter = "TwinCAT Solution (*.sln)|*.sln",
            RestoreDirectory = true
        };
        
        if (saveFileDialog.ShowDialog() == true)
        {
            await CreateSolution(saveFileDialog.FileName);
        }

        BusyState.Reset(this);
    }
        
    private void OpenXmlOnClick(object sender, RoutedEventArgs e)
    {
        BusyState.Set(this);

        var openFileDialog = new OpenFileDialog
        {
            Filter = $"Config file|{XmlFile.DEFAULT_FILE_NAME}",
            RestoreDirectory = true
        };
        
        if (openFileDialog.ShowDialog() == true)
        {
            XmlSelected?.Invoke(openFileDialog.FileName);
        }
        
        BusyState.Reset(this);
    }

    private async Task OpenDte(string path)
    {
        await Task.Run(async () =>
        {
            try
            {
                var dte = new TcDte();
                Logger.LogInfo(this, $"Open project '{path}' ...");

                _solutionIsOpen = false;

                _solutionEvents = dte.SolutionEvents;
                if (_solutionEvents is not null)
                {
                    _solutionEvents.Opened += SolutionEventsOnOpened;
                }
                
                dte.OpenSolution(path);
                while (!_solutionIsOpen) await Task.Delay(100);
                SelectDte(dte);
            }
            catch (Exception e)
            {
                Logger.LogError(this, e.Message);
            }
        });
    }
    
    private void SolutionEventsOnOpened()
    {
        _solutionIsOpen = true;
        if (_solutionEvents is null) return;
        _solutionEvents.Opened -= SolutionEventsOnOpened;
    }

    private async Task CreateSolution(string slnFilePath)
    {
        const string templateName = "OC.TcTemplate";
        var rootFolder = Path.GetDirectoryName(slnFilePath);
        var projectName = Path.GetFileNameWithoutExtension(slnFilePath);

        try
        {
            if (rootFolder is null)
            {
                throw new ArgumentNullException(rootFolder);
            }
            
            //Get zip file from resource
            var assembly = typeof(FileMenu).Assembly;
            var resourceName = $"{assembly.GetName().Name}.Resources.{templateName}.zip";
            var resourceStream = assembly.GetManifestResourceStream(resourceName);
            if (resourceStream is null)
            {
                throw new ArgumentNullException(resourceName);
            }
            
            //Extract resource to folder
            ZipFile.ExtractToDirectory(resourceStream, rootFolder);

            //Rename solution file
            File.Move($"{rootFolder}\\{templateName}.sln", slnFilePath);
                
            //Modify solution file
            var slnFileText = await File.ReadAllTextAsync(slnFilePath);
            await File.WriteAllTextAsync(slnFilePath, slnFileText.Replace(templateName, projectName));
                
            //Rename project folder
            Directory.Move($"{rootFolder}\\{templateName}", $"{rootFolder}\\{projectName}");
                
            //Rename project file
            File.Move($@"{rootFolder}\{projectName}\{templateName}.tsproj", $@"{rootFolder}\{projectName}\{projectName}.tsproj");
        }
        catch(Exception e)
        {
            Logger.LogError(this, e.Message);
            return;
        }

        await OpenDte(slnFilePath);
    }
}