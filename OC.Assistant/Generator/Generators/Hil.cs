﻿using OC.Assistant.Core;
using OC.Assistant.Core.TwinCat;
using OC.Assistant.Generator.EtherCat;
using OC.Assistant.Generator.Profinet;
using TCatSysManagerLib;

namespace OC.Assistant.Generator.Generators;

/// <summary>
/// Generator for HiL signals.
/// </summary>
internal static class Hil
{
    private const string FOLDER_NAME = nameof(Hil);

    /// <summary>
    /// Updates all HiL structures.
    /// </summary>
    /// <param name="projectConnector">The interface of the connected project.</param>
    /// <param name="plcProjectItem">The given plc project.</param>
    public static void Update(IProjectConnector projectConnector, ITcSmTreeItem plcProjectItem)
    {
        XmlFile.ClearHilPrograms();
        if (plcProjectItem.TryLookupChild(FOLDER_NAME) is not null) plcProjectItem.DeleteChild(FOLDER_NAME);
        new ProfinetGenerator(projectConnector, FOLDER_NAME).Generate(plcProjectItem);
        new EtherCatGenerator(projectConnector, FOLDER_NAME).Generate(plcProjectItem);
    }
}