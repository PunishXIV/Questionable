using Questionable.Model.Common.Converter;
using System.Collections.Generic;

namespace Questionable.Model.Questing.Converter;

public sealed class ActionConverter() : EnumConverter<EAction>(Values)
{
    private static readonly Dictionary<EAction, string> Values = new()
    {
        { EAction.DutyAction1, "Duty Action I" },
        { EAction.DutyAction2, "Duty Action II" },
        { EAction.HeavySwing, "Heavy Swing" },
        { EAction.Bootshine, "Bootshine" },
        { EAction.TwinSnakes, "Twin Snakes" },
        { EAction.Demolish, "Demolish" },
        { EAction.DragonKick, "Dragon Kick" },
        { EAction.HeavyShot, "Heavy Shot" },
        { EAction.Cure, "Cure" },
        { EAction.Cure2, "Cure II" },
        { EAction.Eukrasia, "Eukrasia" },
        { EAction.Diagnosis, "Diagnosis" },
        { EAction.EukrasianDiagnosis, "Eukrasian Diagnosis" },
        { EAction.Esuna, "Esuna" },
        { EAction.Physick, "Physick" },
        { EAction.AspectedBenefic, "Aspected Benefic" },
        { EAction.FormShift, "Form Shift" },
        { EAction.FieryBreath, "Fiery Breath" },
        { EAction.BuffetSanuwa, "Buffet (Sanuwa)" },
        { EAction.BuffetGriffin, "Buffet (Griffin)" },
        { EAction.Trample, "Trample" },
        { EAction.Fumigate, "Fumigate" },
        { EAction.Roar, "Roar" },
        { EAction.Seed, "Seed" },
        { EAction.Inhale, "Inhale" },
        { EAction.SiphonSnout, "Siphon Snout" },
        { EAction.PeculiarLight, "Peculiar Light" },
        { EAction.Cannonfire, "Cannonfire" },
        { EAction.RedGulal, "Red Gulal" },
        { EAction.YellowGulal, "Yellow Gulal" },
        { EAction.BlueGulal, "Blue Gulal" },
        { EAction.ElectrixFlux, "Electric Flux" },
        { EAction.HopStep, "Hop-step" },
        { EAction.Hide, "Hide" },
        { EAction.FumaShuriken, "Fuma Shuriken" },
        { EAction.Katon, "Katon" },
        { EAction.Raiton, "Raiton" },
        { EAction.SlugShot, "Slug Shot" },
        { EAction.BosomBrook, "Bosom Brook" },
        { EAction.Souleater, "Souleater" },
        { EAction.Fire3, "Fire III" },
        { EAction.Adloquium, "Adloquium" },
        { EAction.WaterCannon, "Water Cannon" },
        { EAction.Wasshoi, "Wasshoi" },
        { EAction.ShroudedLuminescence, "Shrouded Luminescence" },
        { EAction.BigSneeze, "Big Sneeze" }
    };
}
