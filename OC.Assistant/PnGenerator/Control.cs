﻿using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Xml.Linq;
using OC.Assistant.Core;
using OC.Assistant.Core.TwinCat;
using OC.Assistant.Sdk;
using TCatSysManagerLib;

namespace OC.Assistant.PnGenerator;

public class Control(string scannerTool) : ControlBase
{
    private Settings _settings;

    /// <summary>
    /// Starts capturing.
    /// </summary>
    internal void StartCapture(Settings settings)
    {
        if (IsBusy) return;
        _settings = settings;
            
        if (_settings.PnName == "")
        {
            Logger.LogError(this, "Empty profinet name");
            return;
        }
            
        if (_settings.Adapter is null)
        {
            Logger.LogError(this, "No adapter selected");
            return;
        }

        Task.Run(() =>
        {
            IsBusy = true;
            RunScanner();
            ImportPnDevice();
            IsBusy = false;
        });
    }

    /// <summary>
    /// Runs the scanner application.
    /// </summary>
    private void RunScanner()
    {
        const int duration = 60;
        Logger.LogInfo(this, $"Running {scannerTool}. This will take about {duration} seconds...");
            
        var filePath = $"{TcProjectFolder}\\{_settings.PnName}.xti";
        
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "cmd",
            Arguments = $"/c {scannerTool} -d \"{_settings.Adapter?.Id}\" -t {duration} -o \"{filePath}\""
        };

        try
        {
            process.Start();
            process.WaitForExit();
            Logger.LogInfo(this, $"{scannerTool} has finished");
        }
        catch (Exception e)
        {
            Logger.LogError(this, e.Message);
        }
    }

    /// <summary>
    /// Imports a xti-file.
    /// </summary>
    private void ImportPnDevice()
    {
        //Save TwinCAT project first
        TcDte?.SaveAll();

        //No file found
        var xtiFilePath = $"{TcProjectFolder}\\{_settings.PnName}.xti";
        if (!File.Exists(xtiFilePath))
        {
            Logger.LogInfo(this, "Nothing created");
            return;
        }

        //File is empty
        if (File.ReadAllText(xtiFilePath) == string.Empty)
        {
            Logger.LogInfo(this, "Nothing created");
            File.Delete(xtiFilePath);
            return;
        }
        
        //Update xti file if necessary
        if (_settings.HwFilePath is not null)
        {
            new XtiUpdater().Run(xtiFilePath, _settings.HwFilePath);
        }
            
        //Import and delete xti file 
        Logger.LogInfo(this, $"Import {xtiFilePath}...");
        var tcPnDevice = TcSysManager?.UpdateIoDevice(_settings.PnName, xtiFilePath);
        File.Delete(xtiFilePath);
            
        UpdateTcPnDevice(tcPnDevice);
            
        Logger.LogInfo(this, "Finished");
    }
        
    /// <summary>
    /// Updates the PN-Device.
    /// </summary>
    private void UpdateTcPnDevice(ITcSmTreeItem? tcPnDevice)
    {
        if (tcPnDevice is null) return;
        
        // Add the *.hwml filename for information
        if (_settings.HwFilePath != "") tcPnDevice.Comment = _settings.HwFilePath;
            
        // Set the network adapter
        var deviceDesc = $"{_settings.Adapter?.Name} ({_settings.Adapter?.Description})";
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if ($"{adapter.Name} ({adapter.Description})" != deviceDesc) continue;
            var pnDevice = XDocument.Parse(tcPnDevice.ProduceXml());
            var pnp = pnDevice.Root?.Element("DeviceDef")?.Element("AddressInfo")?.Element("Pnp");
            if (pnp is null) break;
            pnp.Element("DeviceDesc")!.Value = deviceDesc;
            pnp.Element("DeviceName")!.Value = $"\\DEVICE\\{adapter.Id}";
            pnp.Element("DeviceData")!.Value = adapter.GetPhysicalAddress().ToString();
            tcPnDevice.ConsumeXml(pnDevice.ToString());
            break;
        }
    }
    
    public override void OnConnect()
    {
    }

    public override void OnDisconnect()
    {
    }

    public override void OnTcStopped()
    {
    }

    public override void OnTcStarted()
    {
    }
}