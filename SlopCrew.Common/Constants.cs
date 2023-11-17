﻿using System.Collections.Generic;
using SlopCrew.Common.Proto;

namespace SlopCrew.Common;

public class Constants {
    public const uint NetworkVersion = 4;
    public const int MaxCustomCharacterInfo = 5;
    public const int MaxCustomPacketSize = 512;

    public const string DefaultName = "Big Slopper";
    public const string CensoredName = "Punished Slopper";
    public const int NameLimit = 32;

    public const int PingFrequency = 5000;
    public const int ReconnectFrequency = 5000;

    public const int SimpleEncounterStartTime = 3;
    public const int SimpleEncounterEndTime = 5;
    public const int LobbyMaxWaitTime = 30;
    public const int LobbyIncrementWaitTime = 5;
    public const int RaceEncounterStartTime = 3;
    public const int MaxRaceTime = 120;

    public static Dictionary<QuickChatCategory, List<string>> QuickChatMessages = new() {
        {QuickChatCategory.General, ["Heya!"]},
        {QuickChatCategory.Actions, ["Let's battle!"]},
        {QuickChatCategory.Places, ["Jakes"]}
    };
}
