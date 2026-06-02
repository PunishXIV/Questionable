namespace Questionable.Data;

/// <summary>
/// Command ids for <see cref="FFXIVClientStructs.FFXIV.Client.Game.GameMain.ExecuteCommand"/>
/// </summary>
// https://github.com/AtmoOmen/OmenTools/blob/main/Infos/ExecuteCommandFlag.cs
// https://github.com/Jaksuhn/clib/blob/main/Enums/CommandFlag.cs
public enum GameCommand {
    None = 0,
    /// <summary>
    /// Draw or sheathe weapon
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: 1 - Draw, 0 - Sheathe</para>
    /// <para><c>param2</c>: Unknown, fixed at 1</para>
    /// </remarks>
    DrawOrSheatheWeapon = 1,

    /// <summary>
    /// Auto attack
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Whether to enable auto attack (0 - No, 1 - Yes)</para>
    /// <para><c>param2</c>: Target object ID</para>
    /// <para><c>param3</c>: Whether it is manual operation (0 - No, 1 - Yes)</para>
    /// </remarks>
    AutoAttack = 2,

    /// <summary>
    /// Select target
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Target Entity ID (no target: 0xE0000000)</para>
    /// </remarks>
    Target = 3,

    /// <summary>
    /// PVP quick chat
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: QuickChat Row ID</para>
    /// <para><c>param2</c>: Parameter 1</para>
    /// <para><c>param3</c>: Parameter 2</para>
    /// </remarks>
    PVPQuickChat = 5,

    /// <summary>
    /// Dismount
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: 0 - Do not enter queue; 1 - Enter queue</para>
    /// </remarks>
    Dismount = 101,

    /// <summary>
    /// Summon pet
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Pet ID</para>
    /// </remarks>
    SummonPet = 102,

    /// <summary>
    /// Withdraw pet
    /// </summary>
    WithdrawPet = 103,

    /// <summary>
    /// Remove specified status effect from self
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Status ID</para>
    /// <para><c>param3</c>: Status source GameObjectID, or use 0xE0000000 to remove the first status of this type from any source</para>
    /// </remarks>
    StatusOff = 104,

    /// <summary>
    /// Cancel cast
    /// </summary>
    CancelCast = 105,

    /// <summary>
    /// Ride pillion
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Target ID</para>
    /// <para><c>param2</c>: Position index</para>
    /// </remarks>
    RidePillion = 106,

    /// <summary>
    /// Withdraw fashion accessory
    /// </summary>
    WithdrawParasol109 = 109,

    /// <summary>
    /// Withdraw fashion accessory
    /// </summary>
    WithdrawParasol110 = 110,

    /// <summary>
    /// Revive
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Operation (5 - Accept revive; 8 - Confirm return to return point)</para>
    /// </remarks>
    Revive = 200,

    /// <summary>
    /// Territory transport
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Transport method</para>
    /// <list type="table">
    ///     <item>
    ///         <term>1</term>
    ///         <description>NPC teleport</description>
    ///     </item>
    ///     <item>
    ///         <term>3</term>
    ///         <description>Boundary transition</description>
    ///     </item>
    ///     <item>
    ///         <term>4</term>
    ///         <description>Normal teleport</description>
    ///     </item>
    ///     <item>
    ///         <term>7</term>
    ///         <description>Return</description>
    ///     </item>
    ///     <item>
    ///         <term>15</term>
    ///         <description>City aetheryte</description>
    ///     </item>
    ///     <item>
    ///         <term>20</term>
    ///         <description>Housing area</description>
    ///     </item>
    /// </list>
    /// <para><c>param2</c>: Position change method within territory</para>
    /// <list type="table">
    ///     <item>
    ///         <term>1</term>
    ///         <description>Story transition</description>
    ///     </item>
    ///     <item>
    ///         <term>2</term>
    ///         <description>Return to safe area</description>
    ///     </item>
    ///     <item>
    ///         <term>25</term>
    ///         <description>Duty interior transition</description>
    ///     </item>
    ///     <item>
    ///         <term>26</term>
    ///         <description>Dive</description>
    ///     </item>
    /// </list>
    /// </remarks>
    TerritoryTransport = 201,

    /// <summary>
    /// Teleport to specified aetheryte
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Aetheryte ID</para>
    /// <para><c>param2</c>: Whether to use teleport ticket (0 - No, 1 - Yes)</para>
    /// <para><c>param3</c>: Aetheryte Sub ID</para>
    /// </remarks>
    Teleport = 202,

    /// <summary>
    /// Accept teleport offer
    /// </summary>
    AcceptTeleportOffer = 203,

    /// <summary>
    /// Request friend house teleport information
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Unknown</para>
    /// <para><c>param2</c>: Unknown</para>
    /// </remarks>
    RequestFriendHouseTeleport = 210,

    /// <summary>
    /// Teleport to friend house
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Unknown</para>
    /// <para><c>param2</c>: Unknown</para>
    /// <para><c>param3</c>: Aetheryte ID (Personal house - 61, Free company house - 57)</para>
    /// <para><c>param4</c>: Aetheryte Sub ID (appears to be fixed at 1)</para>
    /// </remarks>
    TeleportToFriendHouse = 211,

    /// <summary>
    /// Return to the nearest safe point on the current map if current race is not Lalafell
    /// </summary>
    ReturnIfNotLalafell = 213,

    /// <summary>
    /// Instantly return to return point, or to duty respawn point if in a duty
    /// </summary>
    InstantReturn = 214,

    /// <summary>
    /// Inspect specified player
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Target object ID</para>
    /// </remarks>
    Inspect = 300,

    /// <summary>
    /// Change equipped title
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Title ID</para>
    /// </remarks>
    ChangeTitle = 302,

    /// <summary>
    /// Request cutscene data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Cutscene index in Cutscene.csv</para>
    /// </remarks>
    RequestCutscene307 = 307,

    /// <summary>
    /// Request challenge log data for specific category
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Category index (starts from 1)</para>
    /// </remarks>
    RequestContentsNoteCategory = 310,

    /// <summary>
    /// Clear field markers
    /// </summary>
    ClearFieldMarkers = 313,

    /// <summary>
    /// Assign or swap Blue Mage action to active slot
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Type (0 - Assign to active slot, 1 - Swap active slot)</para>
    /// <para><c>param2</c>: Slot index (starts from 0, less than 24)</para>
    /// <para><c>param3</c>: Action ID / Slot index (starts from 0, less than 24)</para>
    /// </remarks>
    AssignBLUActionToSlot = 315,

    /// <summary>
    /// Request world travel data
    /// </summary>
    RequestWorldTravel = 316,

    /// <summary>
    /// Place field marker
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Marker index</para>
    /// <para><c>param2</c>: Coordinate X * 1000</para>
    /// <para><c>param3</c>: Coordinate Y * 1000</para>
    /// <para><c>param4</c>: Coordinate Z * 1000</para>
    /// </remarks>
    PlaceFieldMarker = 317,

    /// <summary>
    /// Remove field marker
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Marker index</para>
    /// </remarks>
    RemoveFieldMarker = 318,

    /// <summary>
    /// Reset striking dummy aggro
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Striking dummy Object ID</para>
    /// </remarks>
    ResetStrikingDummy = 319,

    /// <summary>
    /// Set current retainer market item price
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Item slot</para>
    /// <para><c>param2</c>: Item price</para>
    /// </remarks>
    SetRetainerMarketPrice = 400,

    /// <summary>
    /// Request specified inventory data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: (int)InventoryType</para>
    /// </remarks>
    RequestInventory = 405,

    /// <summary>
    /// Enter materia attach state
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Item ID</para>
    /// </remarks>
    EnterMateriaAttachState = 408,

    /// <summary>
    /// Leave materia attach state
    /// </summary>
    LeaveMateriaAttachState = 410,

    /// <summary>
    /// Cancel materia meld request
    /// </summary>
    CancelMateriaMeldRequest = 419,

    /// <summary>
    /// Request armoire data
    /// </summary>
    RequestCabinet = 424,

    /// <summary>
    /// Store item to armoire
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Item index in Cabinet.csv</para>
    /// </remarks>
    StoreToCabinet = 425,

    /// <summary>
    /// Restore item from armoire
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Item index in Cabinet.csv</para>
    /// </remarks>
    RestoreFromCabinet = 426,

    /// <summary>
    /// Repair item
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Inventory Type</para>
    /// <para><c>param2</c>: Inventory Slot</para>
    /// <para><c>param3</c>: Item ID</para>
    /// </remarks>
    RepairItem = 434,

    /// <summary>
    /// Repair all equipped items
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Inventory Type (fixed at 1000)</para>
    /// </remarks>
    RepairEquippedItems = 435,

    /// <summary>
    /// Repair all items
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Category (0 - Main hand/Off hand; 1 - Head/Body/Arms; 2 - Legs/Feet; 3 - Ear/Neck; 4 - Wrist/Ring; 5 - Items)</para>
    /// </remarks>
    RepairAllItems = 436,

    /// <summary>
    /// Extract materia
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Inventory Type</para>
    /// <para><c>param2</c>: Inventory Slot</para>
    /// </remarks>
    ExtractMateria = 437,

    /// <summary>
    /// Change gearset
    /// </summary>
    GearsetChange = 441,

    /// <summary>
    /// Request saddlebag data
    /// </summary>
    RequestSaddleBag = 444,

    /// <summary>
    /// Request reconstruction buyback item data
    /// </summary>
    RequestReconstrcutionBuyBack445 = 445,

    /// <summary>
    /// Request reconstruction buyback item data
    /// </summary>
    RequestReconstrcutionBuyBack446 = 446,

    /// <summary>
    /// Send repair request
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Target Entity ID</para>
    /// </remarks>
    SendRepairRequest = 450,

    /// <summary>
    /// Cancel repair request
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Target Entity ID</para>
    /// </remarks>
    CancelRepairRequest = 453,

    /// <summary>
    /// Interrupt current emote
    /// </summary>
    InterruptEmote = 502,

    /// <summary>
    /// Interrupt current special emote
    /// </summary>
    InterruptEmoteSpecial = 503,

    /// <summary>
    /// Change idle posture
    /// </summary>
    /// <remarks>
    /// <para><c>param2</c>: Posture index</para>
    /// </remarks>
    IdlePostureChange = 505,

    /// <summary>
    /// Enter idle posture
    /// </summary>
    /// <remarks>
    /// <para><c>param2</c>: Posture index</para>
    /// </remarks>
    IdlePostureEnter = 506,

    /// <summary>
    /// Exit idle posture
    /// </summary>
    IdlePostureExit = 507,

    /// <summary>
    /// Enter swim state (also forces dismount)
    /// </summary>
    EnterSwim = 608,

    /// <summary>
    /// Leave swim state
    /// </summary>
    LeaveSwim = 609,

    /// <summary>
    /// Enable/disable mounting restriction
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: 0 - Disable; 1 - Enable</para>
    /// </remarks>
    DisableMounting = 612,

    /// <summary>
    /// Enter flight state
    /// </summary>
    EnterFlight = 616,

    /// <summary>
    /// Craft
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Type (0 - Normal craft, 1 - Quick synthesis; 2 - Craft practice)</para>
    /// <para><c>param2</c>: Recipe ID (in Recipe.csv)</para>
    /// <para><c>param3</c>: Additional parameter (Quick synthesis - quantity, max 255)</para>
    /// </remarks>
    Craft = 700,

    /// <summary>
    /// Fish
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Action (0 - Cast, 1 - Reel, 2 - Hook, 4 - Change bait, 5 - Sit, 10 - Powerful hookset, 11 - Precise hookset, 13 - Patience, 14 - Patience II, 24 - Identical cast, 25 - Mooch)
    /// </para>
    /// <para><c>param2</c>: Additional parameter (If changing bait, item ID; If mooching, bait index)</para>
    /// </remarks>
    Fish = 701,

    /// <summary>
    /// Load craft log data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Class index (left to right, starts from 0, ends at 7)</para>
    /// </remarks>
    LoadCraftLog = 710,

    /// <summary>
    /// Exit craft
    /// </summary>
    ExitCraft = 711,

    /// <summary>
    /// Abandon quest
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Quest ID (not RowID)</para>
    /// </remarks>
    AbandonQuest = 800,

    /// <summary>
    /// Refresh leve quest status
    /// </summary>
    RefreshLeveQuest = 801,

    /// <summary>
    /// Abandon leve quest
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Leve quest ID</para>
    /// </remarks>
    AbandonLeveQuest = 802,

    /// <summary>
    /// Start leve quest
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Leve quest ID</para>
    /// <para><c>param2</c>: Level increase amount</para>
    /// </remarks>
    StartLeveQuest = 804,

    /// <summary>
    /// Content related
    /// </summary>
    Content = 808,

    /// <summary>
    /// Start specified FATE
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: FATE ID</para>
    /// <para><c>param2</c>: Target Object ID</para>
    /// </remarks>
    FateStart = 809,

    /// <summary>
    /// Load FATE information
    /// (When switching maps, all FATE information in the map will be loaded at once)
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: FATE ID</para>
    /// </remarks>
    FateLoad = 810,

    /// <summary>
    /// Enter FATE range (This command will not be sent if FATE spawns directly underfoot)
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: FATE ID</para>
    /// </remarks>
    FateEnter = 812,

    /// <summary>
    /// Level sync for FATE
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: FATE ID</para>
    /// <para><c>param2</c>: Whether to level sync (0 - No, 1 - Yes)</para>
    /// </remarks>
    FateLevelSync = 813,

    /// <summary>
    /// FATE mob spawn
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Object ID</para>
    /// </remarks>
    FateMobSpawn = 814,

    /// <summary>
    /// Territory transport finish
    /// </summary>
    TerritoryTransportFinish = 816,

    /// <summary>
    /// Leave duty
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Type (0 - Normal exit, 1 - Inactive for a period)</para>
    /// </remarks>
    LeaveDuty = 819,

    /// <summary>
    /// Send solo quest battle request
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Difficulty (0 - Normal, 1 - Easy, 2 - Very Easy)</para>
    /// </remarks>
    StartSoloQuestBattle = 823,

    /// <summary>
    /// New Game+ mode
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: New Game+ chapter index in QuestRedoChapter.csv (0 - Exit New Game+)</para>
    /// </remarks>
    QuestRedo = 824,

    /// <summary>
    /// Refresh inventory
    /// </summary>
    InventoryRefresh = 830,

    /// <summary>
    /// Request cutscene data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Cutscene index in Cutscene.csv</para>
    /// </remarks>
    RequestCutscene831 = 831,

    /// <summary>
    /// Request achievement progress data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Achievement index in Achievement.csv</para>
    /// </remarks>
    RequestAchievement = 1000,

    /// <summary>
    /// Request all achievement overview (excluding specific achievement content)
    /// </summary>
    RequestAllAchievement = 1001,

    /// <summary>
    /// Request near completion achievement overview (excluding specific achievement content)
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Unknown, fixed at 1</para>
    /// </remarks>
    RequestNearCompletionAchievement = 1002,

    /// <summary>
    /// Request lottery data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Territory Type</para>
    /// <para><c>param2</c>: Plot index</para>
    /// <code>
    /// <![CDATA[
    /// const int HousesPerArea = 60;
    /// const int AreaOffset = 256;
    /// 
    /// // Ward 1, Plot 41
    /// var wardID = 0;
    /// var districtOffset = wardID * AreaOffset;
    /// var houseID = 40;
    /// var position = districtOffset + houseID]]>
    /// </code>
    /// </remarks>
    RequestLotteryData = 1105,

    /// <summary>
    /// Request placard data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Territory Type</para>
    /// <para><c>param2</c>: Plot index</para>
    /// <code>
    /// <![CDATA[
    /// const int HousesPerArea = 60;
    /// const int AreaOffset = 256;
    /// 
    /// // Ward 1, Plot 41
    /// var wardID = 0;
    /// var districtOffset = wardID * AreaOffset;
    /// var houseID = 40;
    /// var position = districtOffset + houseID]]>
    /// </code>
    /// </remarks>
    RequestPlacardData = 1106,

    /// <summary>
    /// Request housing area data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Territory Type</para>
    /// <para><c>param2</c>: Ward index</para>
    /// </remarks>
    RequestHousingAreaData = 1107,

    /// <summary>
    /// Store specified item to house storage
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: High 32 bits of HouseID address for HouseManager related area</para>
    /// <code>*(long*)((nint)HousingManager.Instance()->IndoorTerritory + 38560) >> 32</code>
    /// <para><c>param2</c>: HouseID for HouseManager related area</para>
    /// <para><c>param3</c>: InventoryType</para>
    /// <para><c>param4</c>: InventorySlot</para>
    /// </remarks>
    StoreFurniture = 1112,

    /// <summary>
    /// Restore specified furniture from house
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: High 32 bits of HouseID address for HouseManager related area</para>
    /// <code>(long)((nint)HousingManager.Instance()->IndoorTerritory + 38560) >> 32</code>
    /// <para><c>param2</c>: HouseID for HouseManager related area</para>
    /// <para><c>param3</c>: InventoryType (25000 to 25010 / 27000 to 27008)</para>
    /// <para><c>param4</c>: InventorySlot (If >65535, then furniture at slot (i - 65536) is stored to storage)</para>
    /// </remarks>
    RestoreFurniture = 1113,

    /// <summary>
    /// Request housing name setting data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: High 32 bits of HouseID address for HouseManager related area</para>
    /// <code>*(long*)((nint)HousingManager.Instance()->IndoorTerritory + 38560) >> 32</code>
    /// <para><c>param2</c>: HouseID for HouseManager related area</para>
    /// </remarks>
    RequestHousingName = 1114,

    /// <summary>
    /// Request housing greeting setting data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: HouseManager 相关区域的 HouseID 地址的高 32 位</para>
    /// <code>*(long*)((nint)HousingManager.Instance()->IndoorTerritory + 38560) >> 32</code>
    /// <para><c>param2</c>: HouseManager 相关区域的 HouseID</para>
    /// </remarks>
    RequestHousingGreeting = 1115,

    /// <summary>
    /// Request housing guest access setting data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: HouseManager 相关区域的 HouseID 地址的高 32 位</para>
    /// <code>*(long*)((nint)HousingManager.Instance()->IndoorTerritory + 38560) >> 32</code>
    /// <para><c>param2</c>: HouseManager 相关区域的 HouseID</para>
    /// </remarks>
    RequestHousingGuestAccess = 1117,

    /// <summary>
    /// Save housing guest access settings
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: High 32 bits of HouseID address for HouseManager related area</para>
    /// <code>*(long*)((nint)HousingManager.Instance()->IndoorTerritory + 38560) >> 32</code>
    /// <para><c>param2</c>: HouseID for HouseManager related area</para>
    /// <para><c>param3</c>: Setting enum value combination (Known: 1 - Teleport permission; 65536 - Entry permission)</para>
    /// </remarks>
    SaveHousingGuestAccess = 1118,

    /// <summary>
    /// Request housing estate tag setting data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: HouseManager 相关区域的 HouseID 地址的高 32 位</para>
    /// <code>*(long*)((nint)HousingManager.Instance()->IndoorTerritory + 38560) >> 32</code>
    /// <para><c>param2</c>: HouseManager 相关区域的 HouseID</para>
    /// </remarks>
    RequestHousingEstateTag = 1119,

    /// <summary>
    /// Save housing estate tag settings
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: High 32 bits of HouseID address for HouseManager related area</para>
    /// <code>*(long*)((nint)HousingManager.Instance()->IndoorTerritory + 38560) >> 32</code>
    /// <para><c>param2</c>: HouseID for HouseManager related area</para>
    /// <para><c>param3</c>: Setting enum value combination (Note: Even Tags with the same name have different enum values at different positions)</para>
    /// </remarks>
    SaveHousingEstateTag = 1120,

    /// <summary>
    /// Move to house front gate
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Plot index</para>
    /// </remarks>
    MoveToHouseFrontGate = 1122,

    /// <summary>
    /// Enter "Place furniture/outdoor furnishing" state
    /// </summary>
    /// <remarks>
    /// <para><c>param2</c>: House plot index (0 for apartment)</para>
    /// </remarks>
    FurnishState = 1123,

    /// <summary>
    /// View house detail
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Territory Type</para>
    /// <para><c>param2</c>: Plot index</para>
    /// <code>
    /// <![CDATA[
    /// const int HousesPerArea = 60;
    /// const int AreaOffset = 256;
    /// 
    /// // Ward 1, Plot 41
    /// var wardID = 0;
    /// var districtOffset = wardID * AreaOffset;
    /// var houseID = 40;
    /// var position = districtOffset + houseID]]>
    /// </code>
    /// <para><c>param3</c>: (If applicable) Apartment room index</para>
    /// </remarks>
    ViewHouseDetail = 1126,

    /// <summary>
    /// Adjust house lighting
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Brightness level (0 - Brightest, 5 - Darkest)</para>
    /// </remarks>
    AdjustHouseLight = 1137,

    /// <summary>
    /// Refresh free company material delivery information
    /// </summary>
    RefreshFCMaterialDelivery = 1143,

    /// <summary>
    /// Refresh submarine completion information
    /// </summary>
    RefreshSubmarineInfo = 1144,

    /// <summary>
    /// Set house background music
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Orchestrion roll index in Orchestrion.csv</para>
    /// </remarks>
    SetHouseBackgroundMusic = 1145,

    /// <summary>
    /// Retrieve and place specified item from house storage
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: High 32 bits of HouseID address for HouseManager related area</para>
    /// <code>*(long*)((nint)HousingManager.Instance()->IndoorTerritory + 38560) >> 32</code>
    /// <para><c>param2</c>: HouseID for HouseManager related area</para>
    /// <para><c>param3</c>: InventoryType (25000 to 25010 / 27000 to 27008)</para>
    /// <para><c>param4</c>: InventorySlot</para>
    /// </remarks>
    Furnish = 1150,

    /// <summary>
    /// Repair submarine part
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Submarine index</para>
    /// <para><c>param2</c>: Submarine part index</para>
    /// </remarks>
    RepairSubmarinePart = 1153,

    /// <summary>
    /// Request house interior design information
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: House index (starts from 0, ends at 59)</para>
    /// </remarks>
    HouseInteriorDesignRequest = 1169,

    /// <summary>
    /// Change house interior design style
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: House index (starts from 0, ends at 59)</para>
    /// <para><c>param2</c>: Interior design style (3 - Mist style; 6 - Lavender Beds style; 9 - Goblet style; 12 - Shirogane style; 15 - Empyreum style; 18 - Simple style)</para>
    /// </remarks>
    HouseInteriorDesignChange = 1170,

    /// <summary>
    /// Collect trophy crystal
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Season (0 - Current season; 1 - Previous season)</para>
    /// </remarks>
    CollectTrophyCrystal = 1200,

    /// <summary>
    /// Select PVP role action
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Role action index</para>
    /// </remarks>
    SelectPVPRoleAction = 1201,

    /// <summary>
    /// Request challenge log data
    /// </summary>
    RequestContentsNote = 1301,

    /// <summary>
    /// Request retainer venture time information
    /// </summary>
    RequestRetainerVentureTime = 1400,

    /// <summary>
    /// Repair item at NPC
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Inventory Type</para>
    /// <para><c>param2</c>: Inventory Slot</para>
    /// <para><c>param3</c>: Item ID</para>
    /// </remarks>
    RepairItemNPC = 1600,

    /// <summary>
    /// Repair all items at NPC
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Category (0 - Main hand/Off hand; 1 - Head/Body/Arms; 2 - Legs/Feet; 3 - Ear/Neck; 4 - Wrist/Ring; 5 - Items)</para>
    /// </remarks>
    RepairAllItemsNPC = 1601,

    /// <summary>
    /// Repair all equipped items at NPC
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Inventory Type (fixed at 1000)</para>
    /// </remarks>
    RepairEquippedItemsNPC = 1602,

    /// <summary>
    /// Switch chocobo combat stance
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Index in BuddyAction.csv</para>
    /// </remarks>
    BuddyAction = 1700,

    /// <summary>
    /// Chocobo barding
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Part (0 - Head, 1 - Body, 2 - Legs)</para>
    /// <para><c>param2</c>: Equipment index in BuddyEquip.csv (0 - Remove equipment)</para>
    /// </remarks>
    BuddyEquip = 1701,

    /// <summary>
    /// Chocobo learn skill
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Skill index</para>
    /// </remarks>
    BuddyLearnSkill = 1702,

    /// <summary>
    /// Request Gold Saucer panel general information
    /// </summary>
    RequestGSGeneral = 1850,

    /// <summary>
    /// Request Gold Saucer panel chocobo information
    /// </summary>
    RequestGSChocobo = 1900,

    /// <summary>
    /// Start duty record
    /// </summary>
    StartDutyRecord = 1980,

    /// <summary>
    /// End duty record
    /// </summary>
    EndDutyRecord = 1981,

    /// <summary>
    /// Request Gold Saucer panel Lord of Verminion information
    /// </summary>
    RequestGSLordofVerminion = 2010,

    /// <summary>
    /// Enable/disable auto join novice network setting
    /// </summary>
    EnableAutoJoinNoviceNetwork = 2102,

    /// <summary>
    /// Send duel request
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Target GameObject ID</para>
    /// </remarks>
    SendDuel = 2200,

    /// <summary>
    /// Confirm duel request
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: 0 - Confirm; 1 - Cancel; 4 - Force cancel</para>
    /// </remarks>
    RequestDuel = 2201,

    /// <summary>
    /// Confirm duel
    /// </summary>
    ConfirmDuel = 2202,

    /// <summary>
    /// Confirm Wondrous Tails result
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Index (left to right, top to bottom, starts from 0)</para>
    /// </remarks>
    WondrousTailsConfirm = 2253,

    /// <summary>
    /// Wondrous Tails other operations
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Operation (0 - Think again)</para>
    /// <para><c>param2</c>: Index (left to right, top to bottom, starts from 0)</para>
    /// </remarks>
#pragma warning disable CA1069 // Enums values should not be duplicated
    WondrousTailsOperate = 2253,
#pragma warning restore CA1069 // Enums values should not be duplicated

    /// <summary>
    /// Request prism box data
    /// </summary>
    RequestPrismBox = 2350,

    /// <summary>
    /// Restore prism box item
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Prism box internal item ID (MirageManager.Instance().PrismBoxItemIds)</para>
    /// </remarks>
    RestorePrsimBoxItem = 2352,

    /// <summary>
    /// Request glamour plates data
    /// </summary>
    RequestGlamourPlates = 2355,

    /// <summary>
    /// Enter/exit glamour plate selection state
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: 0 - Exit, 1 - Enter</para>
    /// <para><c>param2</c>: Unknown, possibly 0 or 1</para>
    /// </remarks>
    EnterGlamourPlateState = 2356,

    /// <summary>
    /// Apply glamour plate (must enter glamour plate selection state first)
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Glamour plate index</para>
    /// </remarks>
    ApplyGlamourPlate = 2357,

    /// <summary>
    /// Get Fashion Report weekly participation reward
    /// </summary>
    FashionCheckEntryReward = 2450,

    /// <summary>
    /// Get Fashion Report weekly bonus reward
    /// </summary>
    FashionCheckBonusReward = 2451,

    /// <summary>
    /// Buy back reconstruction item
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Item index</para>
    /// </remarks>
    BuybackReconstrcutionItem = 2501,

    /// <summary>
    /// Request Gold Saucer panel Mahjong information
    /// </summary>
    RequestGSMahjong = 2550,

    /// <summary>
    /// Request Blue Mage spellbook data
    /// </summary>
    RequstAOZNotebook = 2601,

    /// <summary>
    /// Request Trust data
    /// </summary>
    RequestTrustedFriend = 2651,

    /// <summary>
    /// Request Duty Support data
    /// </summary>
    RequestDutySupport = 2653,

    /// <summary>
    /// Send Duty Support application request
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: DawnStory index</para>
    /// <para><c>param2</c>: First four DawnStoryMemberUIParam indices as powers (a1 * 256^0 + a2 * 256^1 + a3 * 256^2 + a4 * 256^3)</para>
    /// <para><c>param3</c>: Last three DawnStoryMemberUIParam indices as powers (a1 * 256^0 + a2 * 256^1 + a3 * 256^2)</para>
    /// </remarks>
    SendDutySupport = 2654,

    /// <summary>
    /// Desynthesize specified item / Recover materia from specified item / Extract from specified item
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Desynthesize: 3735552; Recover materia: 3735553; Extract: 3735554; Repair: 3735555</para>
    /// <para><c>param2</c>: Inventory Type</para>
    /// <para><c>param3</c>: Inventory Slot</para>
    /// <para><c>param4</c>: Item ID</para>
    /// </remarks>
    Desynthesize = 2800,

    /// <summary>
    /// Bozja assign lost action from holster to slot
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Lost action holster index</para>
    /// <para><c>param2</c>: Slot to assign to</para>
    /// </remarks>
    BozjaUseFromHolster = 2950,

    /// <summary>
    /// Request portraits list data
    /// </summary>
    RequestPortraits = 3200,

    /// <summary>
    /// Switch Island Sanctuary mode
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Mode (0 - Free; 1 - Harvest; 2 - Plant; 3 - Water; 4 - Remove; 6 - Feed; 7 - Pet; 8 - Call; 9 - Capture)</para>
    /// </remarks>
    MJISetMode = 3250,

    /// <summary>
    /// Set Island Sanctuary mode parameter, set to 0 when switching modes, set to corresponding item ID when planting, feeding, or capturing
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Parameter</para>
    /// </remarks>
    MJISetModeParam = 3251,

    /// <summary>
    /// Island Sanctuary settings panel toggle
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: State (1 - Open; 0 - Close)</para>
    /// </remarks>
    MJISettingPanelToggle = 3252,

    /// <summary>
    /// Request Island Sanctuary workshop schedule data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Specific day (0 is first day of current cycle, 7 is first day of next cycle)</para>
    /// </remarks>
    MJIWorkshopRequest = 3254,

    /// <summary>
    /// Request Island Sanctuary workshop schedule item data
    /// </summary>
    MJIWorkshopRequestItem = 3258,

    /// <summary>
    /// Island Sanctuary workshop schedule assignment
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Item and schedule time slot: (8 * (startingHour | (32 * craftObjectId)))</para>
    /// <para><c>param2</c>: Specific day (0 - First day of current cycle, 7 - First day of next cycle)</para>
    /// <para><c>param4</c>: Add/Remove (0 - Add, 1 - Remove)</para>
    /// </remarks>
    MJIWorkshopAssign = 3259,

    /// <summary>
    /// Cancel Island Sanctuary workshop schedule
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Item and schedule time slot: (8 * (startingHour | (32 * craftObjectId)))</para>
    /// <para><c>param2</c>: Specific day (0 - First day of current cycle, 7 - First day of next cycle)</para>
    /// </remarks>
    MJIWorkshopCancel = 3260,

    /// <summary>
    /// Set Island Sanctuary rest cycles
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Rest day 1</para>
    /// <para><c>param2</c>: Rest day 2</para>
    /// <para><c>param3</c>: Rest day 3</para>
    /// <para><c>param4</c>: Rest day 4</para>
    /// </remarks>
    MJISetRestCycles = 3261,

    /// <summary>
    /// Collect Island Sanctuary granary exploration results
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Granary index</para>
    /// </remarks>
    MJIGranaryCollect = 3262,

    /// <summary>
    /// View Island Sanctuary granary exploration destinations
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Granary index</para>
    /// </remarks>
    MJIGranaryViewDestinations = 3263,

    /// <summary>
    /// Island Sanctuary granary dispatch exploration
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Granary index</para>
    /// <para><c>param2</c>: Destination index</para>
    /// <para><c>param3</c>: Exploration days</para>
    /// </remarks>
    MJIGranaryAssign = 3264,

    /// <summary>
    /// Release minion on Island Sanctuary
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Minion ID</para>
    /// <para><c>param2</c>: Release area index</para>
    /// </remarks>
    MJIReleaseMinion = 3265,

    /// <summary>
    /// Release Island Sanctuary pasture animal
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Animal index</para>
    /// </remarks>
    MJIReleaseAnimal = 3268,

    /// <summary>
    /// Collect Island Sanctuary pasture animal leavings
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Animal index</para>
    /// <para><c>param2</c>: Collection flag</para>
    /// </remarks>
    MJICollectAnimalLeavings = 3269,

    /// <summary>
    /// Collect all Island Sanctuary pasture animal leavings
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Expected number of leavings to collect (MJIManager.Instance()->PastureHandler->AvailableMammetLeavings)</para>
    /// </remarks>
    MJICollectAllAnimalLeavings = 3271,

    /// <summary>
    /// Entrust Island Sanctuary pasture animal
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Animal index</para>
    /// <para><c>param2</c>: Feed item ID</para>
    /// </remarks>
    MJIEntrustAnimal = 3272,

    /// <summary>
    /// Recall minion released on Island Sanctuary
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Minion index</para>
    /// </remarks>
    MJIRecallMinion = 3277,

    /// <summary>
    /// Entrust single Island Sanctuary farm plot
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Farm plot index</para>
    /// <para><c>param2</c>: Seed item ID</para>
    /// </remarks>
    MJIFarmEntrustSingle = 3279,

    /// <summary>
    /// Dismiss single Island Sanctuary farm plot
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Farm plot index</para>
    /// </remarks>
    MJIFarmDismiss = 3280,

    /// <summary>
    /// Collect single Island Sanctuary farm plot
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Farm plot index</para>
    /// <para><c>param2</c>: Whether to dismiss after collection (0 - No, 1 - Yes)</para>
    /// </remarks>
    MJIFarmCollectSingle = 3281,

    /// <summary>
    /// Collect all Island Sanctuary farm plots
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: *(int*)MJIManager.Instance()->GranariesState</para>
    /// </remarks>
    MJIFarmCollectAll = 3282,

    /// <summary>
    /// Request Island Sanctuary workshop favor state data
    /// </summary>
    MJIFavorStateRequest = 3292,

    /// <summary>
    /// Change Wonderous Kaiten mode
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Mode index</para>
    /// </remarks>
    WKSChangeMode = 3400,

    /// <summary>
    /// Wonderous Kaiten end interaction 1
    /// </summary>
    WKSEndInteraction1 = 3401,

    /// <summary>
    /// Wonderous Kaiten end interaction 2
    /// </summary>
    WKSEndInteraction2 = 3402,

    /// <summary>
    /// Wonderous Kaiten start mission
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Mission Unit ID</para>
    /// </remarks>
    WKSStartMission = 3440,

    /// <summary>
    /// Wonderous Kaiten complete mission
    /// </summary>
    WKSCompleteMission = 3441,

    /// <summary>
    /// Wonderous Kaiten abandon mission
    /// </summary>
    WKSAbandonMission = 3442,

    /// <summary>
    /// Wonderous Kaiten start lottery
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Type: 0 - Lunar credit; 1 - Faenna credit</para>
    /// </remarks>
    WKSStartLottery = 3450,

    /// <summary>
    /// Wonderous Kaiten choose lottery wheel
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Type: 0 - Lunar credit; 1 - Faenna credit</para>
    /// <para><c>param2</c>: Wheel type (Left - 0, Right - 1)</para>
    /// </remarks>
    WKSChooseLottery = 3451,

    /// <summary>
    /// Wonderous Kaiten end lottery
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Type: 0 - Lunar credit; 1 - Faenna credit</para>
    /// </remarks>
    WKSEndLottery = 3452,

    /// <summary>
    /// Wonderous Kaiten request exploration successes data
    /// </summary>
    WKSRequestSuccesses = 3460,

    /// <summary>
    /// Wonderous Kaiten request mecha data
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: WKSMechaEventData Row ID (0 - Currently not started)</para>
    /// </remarks>
    WKSRequestMecha = 3478,

    /// <summary>
    /// Roll dice
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Type (fixed at 0)</para>
    /// <para><c>param2</c>: Maximum value</para>
    /// </remarks>
    RollDice = 9000,

    /// <summary>
    /// Retainer
    /// </summary>
    Retainer = 9003,

    /// <summary>
    /// Set character display range
    /// </summary>
    /// <remarks>
    /// <para><c>param1</c>: Type (0 - Standard; 1 - Large; 2 - Maximum)</para>
    /// </remarks>
    AroundRangeSetMode = 9005,
}
