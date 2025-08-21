using System.Text.Json.Serialization;
using Questionable.Model.Questing.Converter;

namespace Questionable.Model.Questing;

[JsonConverter(typeof(ActionConverter))]
public enum EAction
{
    DutyAction1 = 26 | 0x10000,
    DutyAction2 = 27 | 0x10000,

    HeavySwing = 31,
    Bootshine = 53,
    TwinSnakes = 61,
    Demolish = 66,
    DragonKick = 74,
    HeavyShot = 97,
    Cure = 120,
    Cure2 = 135,
    Eukrasia = 24290, 
    Diagnosis = 24284,
    EukrasianDiagnosis = 24291,
    Esuna = 7568,
    Physick = 190,
    AspectedBenefic = 3595,
    FormShift = 4262,
    FieryBreath = 1764,
    BuffetSanuwa = 4931,
    BuffetGriffin = 4583,
    Trample = 4585,
    Fumigate = 5872,
    Roar = 6293,
    Seed = 6294,
    MagitekPulse = 8624,
    MagitekThunder = 8625,
    Inhale = 10013,
    SiphonSnout = 18187,
    PeculiarLight = 20030,
    Cannonfire = 20121,
    RedGulal = 29382,
    YellowGulal = 29383,
    BlueGulal = 29384,
    ElectrixFlux = 29718,
    HopStep = 31116,
    Hide = 2245,
    Ten = 2259,
    Ninjutsu = 2260,
    Chi = 2261,
    Jin = 2263,
    FumaShuriken = 2265,
    Katon = 2266,
    Raiton = 2267,
    RabbitMedium = 2272,
    SlugShot = 2868,
    BosomBrook = 37173,
    Souleater = 3632,
    Fire3 = 152,
    Adloquium = 185,
    WaterCannon = 11385,
    Wasshoi = 11499,
    ShroudedLuminescence = 39505,
    BigSneeze = 1765,

    Prospect = 227,
    CollectMiner = 240,
    LuckOfTheMountaineer = 4081,
    ScourMiner = 22182,
    MeticulousMiner = 22184,
    ScrutinyMiner = 22185,

    Triangulate = 210,
    CollectBotanist = 815,
    LuckOfThePioneer = 4095,
    ScourBotanist = 22186,
    MeticulousBotanist = 22188,
    ScrutinyBotanist = 22189,

    SharpVision1 = 235,
    SharpVision2 = 237,
    SharpVision3 = 295,
    FieldMastery1 = 218,
    FieldMastery2 = 220,
    FieldMastery3 = 294,
}

public static class EActionExtensions
{
    public static bool RequiresMount(this EAction action)
    {
        return action
            is EAction.FieryBreath
            or EAction.BuffetSanuwa
            or EAction.BuffetGriffin
            or EAction.Trample
            or EAction.Fumigate
            or EAction.Roar
            or EAction.Seed
            or EAction.Inhale
            or EAction.SiphonSnout
            or EAction.PeculiarLight
            or EAction.Cannonfire
            or EAction.RedGulal
            or EAction.YellowGulal
            or EAction.BlueGulal
            or EAction.ElectrixFlux
            or EAction.HopStep
            or EAction.BosomBrook
            or EAction.Wasshoi
            or EAction.ShroudedLuminescence
            or EAction.BigSneeze;
    }
}
