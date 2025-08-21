using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Questionable.Model.Common;

namespace Questionable.Data;

internal sealed class AetheryteData
{
    public AetheryteData(IDataManager dataManager)
    {
        Dictionary<EAetheryteLocation, ushort> territoryIds = new();
        Dictionary<EAetheryteLocation, ushort> aethernetGroups = new();


        void ConfigureAetheryte(EAetheryteLocation aetheryteLocation, ushort territoryId,
            ushort aethernetGroup)
        {
            territoryIds[aetheryteLocation] = territoryId;
            aethernetGroups[aetheryteLocation] = aethernetGroup;
        }

        void ConfigureAetheryteWithAutoGroup(EAetheryteLocation aetheryteLocation, ushort territoryId)
        {
            ConfigureAetheryte(aetheryteLocation, territoryId, (ushort)((int)aetheryteLocation / 100));
        }

        foreach (var aetheryte in dataManager.GetExcelSheet<Aetheryte>().Where(x => x.RowId > 0))
        {
            if (aetheryte.Territory.RowId > 0)
                territoryIds[(EAetheryteLocation)aetheryte.RowId] = (ushort)aetheryte.Territory.RowId;

            if (aetheryte.AethernetGroup > 0)
                aethernetGroups[(EAetheryteLocation)aetheryte.RowId] = aetheryte.AethernetGroup;
        }

        ConfigureAetheryte(EAetheryteLocation.IshgardFirmament, 886, aethernetGroups[EAetheryteLocation.Ishgard]);
        ConfigureAetheryteWithAutoGroup(EAetheryteLocation.FirmamentMendicantsCourt, 886);
        ConfigureAetheryteWithAutoGroup(EAetheryteLocation.FirmamentMattock, 886);
        ConfigureAetheryteWithAutoGroup(EAetheryteLocation.FirmamentNewNest, 886);
        ConfigureAetheryteWithAutoGroup(EAetheryteLocation.FirmanentSaintRoellesDais, 886);
        ConfigureAetheryteWithAutoGroup(EAetheryteLocation.FirmamentFeatherfall, 886);
        ConfigureAetheryteWithAutoGroup(EAetheryteLocation.FirmamentHoarfrostHall, 886);
        ConfigureAetheryteWithAutoGroup(EAetheryteLocation.FirmamentWesternRisensongQuarter, 886);
        ConfigureAetheryteWithAutoGroup(EAetheryteLocation.FIrmamentEasternRisensongQuarter, 886);

        TerritoryIds = territoryIds.AsReadOnly();
        AethernetGroups = aethernetGroups.AsReadOnly();

        TownTerritoryIds = dataManager.GetExcelSheet<TerritoryType>()
            .Where(x => x.RowId > 0 && !string.IsNullOrEmpty(x.Name.ToString()) && x.TerritoryIntendedUse.RowId == 0)
            .Select(x => (ushort)x.RowId)
            .ToList();
    }

    public ReadOnlyDictionary<EAetheryteLocation, Vector3> Locations { get; } =
        new Dictionary<EAetheryteLocation, Vector3>
            {
                { EAetheryteLocation.Gridania, new(32.913696f, 2.670288f, 30.014404f) },
                { EAetheryteLocation.GridaniaArcher, new(166.58276f, -1.7243042f, 86.13721f) },
                { EAetheryteLocation.GridaniaLeatherworker, new(101.27405f, 9.018005f, -111.31464f) },
                { EAetheryteLocation.GridaniaLancer, new(121.23291f, 12.649658f, -229.63306f) },
                { EAetheryteLocation.GridaniaConjurer, new(-145.15906f, 4.9591064f, -11.7647705f) },
                { EAetheryteLocation.GridaniaBotanist, new(-311.0857f, 7.94989f, -177.05048f) },
                { EAetheryteLocation.GridaniaAmphitheatre, new(-73.92999f, 7.9804688f, -140.15417f) },

                { EAetheryteLocation.CentralShroudBentbranchMeadows, new(13.076904f, 0.56451416f, 35.90442f) },
                { EAetheryteLocation.EastShroudHawthorneHut, new(-186.54156f, 3.7994385f, 297.56616f) },
                { EAetheryteLocation.SouthShroudQuarrymill, new(178.60681f, 10.543945f, -68.19263f) },
                { EAetheryteLocation.SouthShroudCampTranquil, new(-230.0603f, 22.62909f, 355.45886f) },
                { EAetheryteLocation.NorthShroudFallgourdFloat, new(-41.58087f, -38.55963f, 233.7528f) },

                { EAetheryteLocation.Uldah, new(-144.51825f, -1.3580933f, -169.6651f) },
                { EAetheryteLocation.UldahAdventurers, new(64.22522f, 4.5318604f, -115.31244f) },
                { EAetheryteLocation.UldahThaumaturge, new(-154.83331f, 14.633362f, 73.07532f) },
                { EAetheryteLocation.UldahGladiator, new(-53.849182f, 10.696533f, 12.222412f) },
                { EAetheryteLocation.UldahMiner, new(33.49353f, 13.229492f, 113.206665f) },
                { EAetheryteLocation.UldahAlchemist, new(-98.25293f, 42.34375f, 88.45642f) },
                { EAetheryteLocation.UldahWeaver, new(89.64673f, 12.924377f, 58.27417f) },
                { EAetheryteLocation.UldahGoldsmith, new(-19.333252f, 14.602844f, 72.03784f) },
                { EAetheryteLocation.UldahSapphireAvenue, new(131.9447f, 4.714966f, -29.800903f) },
                { EAetheryteLocation.UldahChamberOfRule, new(6.6376343f, 30.655273f, -24.826477f) },

                { EAetheryteLocation.WesternThanalanHorizon, new(68.0094f, 48.203125f, -227.039f) },
                { EAetheryteLocation.CentralThanalanBlackBrushStation, new(-16.159302f, 0.32037354f, -166.58276f) },
                { EAetheryteLocation.EasternThanalanCampDrybone, new(-386.3432f, -57.1756f, 142.59558f) },
                { EAetheryteLocation.SouthernThanalanLittleAlaMhigo, new(-159.3805f, 30.105957f, -415.45746f) },
                { EAetheryteLocation.SouthernThanalanForgottenSprings, new(-326.6194f, 10.696533f, 406.63757f) },
                { EAetheryteLocation.NorthernThanalanCampBluefog, new(20.98108f, 8.8349f, 454.0321f) },
                {
                    EAetheryteLocation.NorthernThanalanCeruleumProcessingPlant,
                    new(-26.596497f, 49.881714f, -30.838562f)
                },

                { EAetheryteLocation.Limsa, new(-84.031494f, 20.767456f, 0.015197754f) },
                { EAetheryteLocation.LimsaAftcastle, new(16.067688f, 40.787354f, 68.80286f) },
                { EAetheryteLocation.LimsaCulinarian, new(-56.50421f, 44.47998f, -131.45648f) },
                { EAetheryteLocation.LimsaArcanist, new(-335.1645f, 12.619202f, 56.381958f) },
                { EAetheryteLocation.LimsaFisher, new(-179.40033f, 4.8065186f, 182.97095f) },
                { EAetheryteLocation.LimsaMarauder, new(-5.1728516f, 44.63257f, -218.06671f) },
                { EAetheryteLocation.LimsaHawkersAlley, new(-213.61108f, 16.739136f, 51.80432f) },

                { EAetheryteLocation.LowerLaNosceaMorabyDrydocks, new(156.11499f, 15.518433f, 673.21277f) },
                { EAetheryteLocation.MiddleLaNosceaSummerfordFarms, new(227.98499f, 115.526f, -257.0382f) },
                { EAetheryteLocation.EasternLaNosceaCostaDelSol, new(489.15845f, 20.828491f, 468.80298f) },
                { EAetheryteLocation.EasternLaNosceaWineport, new(-18.387146f, 72.67859f, 3.829956f) },
                { EAetheryteLocation.WesternLaNosceaSwiftperch, new(651.5449f, 11.734131f, 513.35913f) },
                { EAetheryteLocation.WesternLaNosceaAleport, new(260.94446f, -19.60791f, 218.52441f) },
                { EAetheryteLocation.UpperLaNosceaCampBronzeLake, new(437.4303f, 5.508484f, 94.590576f) },
                { EAetheryteLocation.OuterLaNosceaCampOverlook, new(-117.54028f, 66.02576f, -212.66504f) },

                { EAetheryteLocation.CoerthasCentralHighlandsCampDragonhead, new(223.98718f, 315.7854f, -234.85168f) },
                { EAetheryteLocation.MorDhona, new(40.024292f, 24.002441f, -668.0247f) },
                { EAetheryteLocation.WolvesDenPier, new(40.93994f, 5.4779663f, -14.816589f) },

                { EAetheryteLocation.GoldSaucer, new(-0.015319824f, 3.4942627f, -0.015319824f) },
                { EAetheryteLocation.GoldSaucerEntranceCardSquares, new(-64.74408f, 2.8839111f, 54.33728f) },
                { EAetheryteLocation.GoldSaucerWonderSquareEast, new(59.067627f, 23.88031f, 63.035034f) },
                { EAetheryteLocation.GoldSaucerWonderSquareWest, new(-0.38153076f, 23.88031f, 58.609863f) },
                { EAetheryteLocation.GoldSaucerEventSquare, new(95.47571f, -2.1210327f, -72.3125f) },
                { EAetheryteLocation.GoldSaucerCactpotBoard, new(114.42737f, 13.595764f, -38.864807f) },
                { EAetheryteLocation.GoldSaucerRoundSquare, new(-24.765442f, 6.1798096f, -85.67944f) },
                { EAetheryteLocation.GoldSaucerChocoboSquare, new(-16.037292f, 2.8839111f, -33.432617f) },
                { EAetheryteLocation.GoldSaucerMinionSquare, new(50.736206f, 2.8839111f, 19.912964f) },

                { EAetheryteLocation.Ishgard, new(-63.98114f, 11.154297f, 43.9917f) },
                { EAetheryteLocation.IshgardForgottenKnight, new(45.792236f, 24.551636f, 0.99176025f) },
                { EAetheryteLocation.IshgardSkysteelManufactory, new(-111.436646f, 16.128723f, -27.054321f) },
                { EAetheryteLocation.IshgardBrume, new(49.42395f, -11.154419f, 66.69714f) },
                { EAetheryteLocation.IshgardAthenaeumAstrologicum, new(133.37903f, -8.86554f, -64.77466f) },
                { EAetheryteLocation.IshgardJeweledCrozier, new(-134.6914f, -11.795227f, -15.396423f) },
                { EAetheryteLocation.IshgardSaintReymanaudsCathedral, new(-77.958374f, 10.60498f, -126.54315f) },
                { EAetheryteLocation.IshgardTribunal, new(78.01941f, 11.001709f, -126.51257f) },
                { EAetheryteLocation.IshgardLastVigil, new(0.015197754f, 16.525452f, -32.51703f) },

                { EAetheryteLocation.Idyllshire, new(71.94617f, 211.26111f, -18.905945f) },
                { EAetheryteLocation.IdyllshireWest, new(-75.48645f, 210.22351f, -21.347473f) },

                { EAetheryteLocation.RhalgrsReach, new(78.23291f, 1.9683228f, 97.45935f) },
                { EAetheryteLocation.RhalgrsReachWest, new(-84.275635f, 0.503479f, 9.323181f) },
                { EAetheryteLocation.RhalgrsReachNorthEast, new(101.24353f, 3.463745f, -115.46509f) },

                { EAetheryteLocation.CoerthasWesternHighlandsFalconsNest, new(474.87585f, 217.94458f, 708.5221f) },
                { EAetheryteLocation.SeaOfCloudsCampCloudtop, new(-615.7473f, -118.36426f, 546.5934f) },
                { EAetheryteLocation.SeaOfCloudsOkZundu, new(-613.1533f, -49.485046f, -415.03015f) },
                { EAetheryteLocation.AzysLlaHelix, new(-722.8046f, -182.29956f, -593.40814f) },
                { EAetheryteLocation.DravanianForelandsTailfeather, new(532.6771f, -48.722107f, 30.166992f) },
                { EAetheryteLocation.DravanianForelandsAnyxTrine, new(-304.12756f, -16.70868f, 32.059082f) },
                { EAetheryteLocation.ChurningMistsMoghome, new(259.20496f, -37.70508f, 596.85657f) },
                { EAetheryteLocation.ChurningMistsZenith, new(-584.9546f, 52.84192f, 313.43542f) },

                { EAetheryteLocation.Kugane, new(47.501343f, 8.438171f, -37.30841f) },
                { EAetheryteLocation.KuganeShiokazeHostelry, new(-73.16705f, -6.088379f, -77.77527f) },
                { EAetheryteLocation.KuganePier1, new(-113.57294f, -3.8911133f, 155.41309f) },
                { EAetheryteLocation.KuganeThavnairianConsulate, new(27.17627f, 9.048584f, 141.58838f) },
                { EAetheryteLocation.KuganeMarkets, new(26.687988f, 4.92865f, 73.3501f) },
                { EAetheryteLocation.KuganeBokairoInn, new(-76.00525f, 19.058472f, -161.18109f) },
                { EAetheryteLocation.KuganeRubyBazaar, new(132.40247f, 12.954895f, 83.02429f) },
                { EAetheryteLocation.KuganeSekiseigumiBarracks, new(119.09656f, 13.01593f, -92.881714f) },
                { EAetheryteLocation.KuganeRakuzaDistrict, new(24.64331f, 7.003784f, -152.97174f) },

                { EAetheryteLocation.FringesCastrumOriens, new(-629.11426f, 132.89075f, -509.14783f) },
                { EAetheryteLocation.FringesPeeringStones, new(415.3047f, 117.357056f, 246.75354f) },
                { EAetheryteLocation.PeaksAlaGannha, new(114.579956f, 120.10376f, -747.06647f) },
                { EAetheryteLocation.PeaksAlaGhiri, new(-271.3817f, 259.87634f, 748.86694f) },
                { EAetheryteLocation.LochsPortaPraetoria, new(-652.0333f, 53.391357f, -16.006714f) },
                { EAetheryteLocation.LochsAlaMhiganQuarter, new(612.4512f, 84.45862f, 656.82446f) },
                { EAetheryteLocation.RubySeaTamamizu, new(358.72437f, -118.05908f, -263.4165f) },
                { EAetheryteLocation.RubySeaOnokoro, new(88.181885f, 4.135132f, -583.3677f) },
                { EAetheryteLocation.YanxiaNamai, new(432.66956f, 73.07532f, -90.74542f) },
                { EAetheryteLocation.YanxiaHouseOfTheFierce, new(246.02112f, 9.079041f, -401.3581f) },
                { EAetheryteLocation.AzimSteppeReunion, new(556.1454f, -16.800232f, 340.10828f) },
                { EAetheryteLocation.AzimSteppeDawnThrone, new(78.26355f, 119.37134f, 36.301147f) },
                { EAetheryteLocation.AzimSteppeDhoroIloh, new(-754.63495f, 131.2428f, 116.5636f) },

                { EAetheryteLocation.DomanEnclave, new(42.648926f, 1.4190674f, -14.8776245f) },
                { EAetheryteLocation.DomanEnclaveNorthern, new(8.987488f, 0.8086548f, -105.85187f) },
                { EAetheryteLocation.DomanEnclaveSouthern, new(-61.57019f, 0.77819824f, 90.684326f) },
                { EAetheryteLocation.DomanEnclaveDocks, new(96.269165f, -3.4332886f, 81.01013f) },

                { EAetheryteLocation.Crystarium, new(-65.0188f, 4.5318604f, 0.015197754f) },
                { EAetheryteLocation.CrystariumMarkets, new(-6.149414f, -7.736328f, 148.72961f) },
                { EAetheryteLocation.CrystariumTemenosRookery, new(-107.37775f, -0.015319824f, -58.762512f) },
                { EAetheryteLocation.CrystariumDossalGate, new(64.86609f, -0.015319824f, -18.173523f) },
                { EAetheryteLocation.CrystariumPendants, new(35.477173f, -0.015319824f, 222.58337f) },
                { EAetheryteLocation.CrystariumAmaroLaunch, new(66.60559f, 35.99597f, -131.09033f) },
                { EAetheryteLocation.CrystariumCrystallineMean, new(-52.506348f, 19.97406f, -173.35773f) },
                { EAetheryteLocation.CrystariumCabinetOfCuriosity, new(-54.398438f, -37.70508f, -241.07733f) },

                { EAetheryteLocation.Eulmore, new(0.015197754f, 81.986694f, 0.93078613f) },
                { EAetheryteLocation.EulmoreMainstay, new(10.940674f, 36.087524f, -4.196289f) },
                { EAetheryteLocation.EulmoreNightsoilPots, new(-54.093323f, -0.83929443f, 52.140015f) },
                { EAetheryteLocation.EulmoreGloryGate, new(6.9122925f, 6.240906f, -56.351562f) },
                { EAetheryteLocation.EulmoreSoutheastDerelict, new(71.82422f, -10.391418f, 65.32385f) },

                { EAetheryteLocation.LakelandFortJobb, new(753.7803f, 24.338135f, -28.82434f) },
                { EAetheryteLocation.LakelandOstallImperative, new(-735.01184f, 53.391357f, -230.02979f) },
                { EAetheryteLocation.KholusiaStilltide, new(668.32983f, 29.465088f, 289.17358f) },
                { EAetheryteLocation.KholusiaWright, new(-244.00702f, 20.736938f, 385.45813f) },
                { EAetheryteLocation.KholusiaTomra, new(-426.38287f, 419.27222f, -623.5294f) },
                { EAetheryteLocation.AmhAraengMordSouq, new(246.38745f, 12.985352f, -220.29456f) },
                { EAetheryteLocation.AmhAraengInnAtJourneysHead, new(399.0996f, -24.521301f, 307.97278f) },
                { EAetheryteLocation.AmhAraengTwine, new(-511.3451f, 47.989624f, -212.604f) },
                { EAetheryteLocation.RaktikaSlitherbough, new(-103.4104f, -19.333252f, 297.23047f) },
                { EAetheryteLocation.RaktikaFanow, new(382.77246f, 21.042175f, -194.11005f) },
                { EAetheryteLocation.IlMhegLydhaLran, new(-344.71655f, 48.722046f, 512.2606f) },
                { EAetheryteLocation.IlMhegPlaEnni, new(-72.55664f, 103.95972f, -857.35864f) },
                { EAetheryteLocation.IlMhegWolekdorf, new(380.51416f, 87.20532f, -687.2511f) },
                { EAetheryteLocation.TempestOndoCups, new(561.76074f, 352.62073f, -199.17603f) },
                { EAetheryteLocation.TempestMacarensesAngle, new(-141.74109f, -280.5371f, 218.00562f) },

                { EAetheryteLocation.OldSharlayan, new(0.07623291f, 4.8065186f, -0.10687256f) },
                { EAetheryteLocation.OldSharlayanStudium, new(-291.1574f, 20.004517f, -74.143616f) },
                { EAetheryteLocation.OldSharlayanBaldesionAnnex, new(-92.21033f, 2.304016f, 29.709229f) },
                { EAetheryteLocation.OldSharlayanRostra, new(-36.94214f, 41.367188f, -156.6034f) },
                { EAetheryteLocation.OldSharlayanLeveilleurEstate, new(204.79126f, 21.774597f, -118.73047f) },
                { EAetheryteLocation.OldSharlayanJourneysEnd, new(206.22559f, 1.8463135f, 13.77887f) },
                { EAetheryteLocation.OldSharlayanScholarsHarbor, new(16.494995f, -16.250854f, 127.73328f) },

                { EAetheryteLocation.RadzAtHan, new(25.986084f, 3.250122f, -27.023743f) },
                { EAetheryteLocation.RadzAtHanMeghaduta, new(-365.95715f, 44.99878f, -31.815125f) },
                { EAetheryteLocation.RadzAtHanRuveydahFibers, new(-156.14563f, 35.99597f, 27.725586f) },
                { EAetheryteLocation.RadzAtHanAirship, new(-144.33508f, 27.969727f, 202.2583f) },
                { EAetheryteLocation.RadzAtHanAlzadaalsPeace, new(6.6071167f, -2.02948f, 110.55151f) },
                { EAetheryteLocation.RadzAtHanHallOfTheRadiantHost, new(-141.37488f, 3.982544f, -98.435974f) },
                { EAetheryteLocation.RadzAtHanMehrydesMeyhane, new(-42.61847f, -0.015319824f, -197.61963f) },
                { EAetheryteLocation.RadzAtHanKama, new(129.59485f, 26.993164f, 13.473633f) },
                { EAetheryteLocation.RadzAtHanHighCrucible, new(57.90796f, -24.704407f, -210.6203f) },

                { EAetheryteLocation.FirmamentMendicantsCourt, new(23.941406f, -16.006714f, 169.35986f) },
                { EAetheryteLocation.FirmamentMattock, new(76.035645f, -18.509216f, 10.299805f) },
                { EAetheryteLocation.FirmamentNewNest, new(149.49255f, -50.003845f, 98.55798f) },
                { EAetheryteLocation.FirmanentSaintRoellesDais, new(207.75159f, -40.024475f, -25.589417f) },
                { EAetheryteLocation.FirmamentFeatherfall, new(-78.78235f, -0.015319824f, 75.97461f) },
                { EAetheryteLocation.FirmamentHoarfrostHall, new(-132.55518f, 9.964111f, -14.66394f) },
                { EAetheryteLocation.FirmamentWesternRisensongQuarter, new(-91.722046f, -0.015319824f, -115.19043f) },
                { EAetheryteLocation.FIrmamentEasternRisensongQuarter, new(114.3053f, -20.004639f, -107.43884f) },

                { EAetheryteLocation.LabyrinthosArcheion, new(443.5338f, 170.6416f, -476.18835f) },
                { EAetheryteLocation.LabyrinthosSharlayanHamlet, new(8.377136f, -27.542603f, -46.67737f) },
                { EAetheryteLocation.LabyrinthosAporia, new(-729.18286f, -27.634155f, 302.1438f) },
                { EAetheryteLocation.ThavnairYedlihmad, new(193.49963f, 6.9733276f, 629.2362f) },
                { EAetheryteLocation.ThavnairGreatWork, new(-527.48914f, 4.776001f, 36.75891f) },
                { EAetheryteLocation.ThavnairPalakasStand, new(405.1422f, 5.2643433f, -244.4953f) },
                { EAetheryteLocation.GarlemaldCampBrokenGlass, new(-408.10254f, 24.15503f, 479.9724f) },
                { EAetheryteLocation.GarlemaldTertium, new(518.9136f, -35.324707f, -178.36273f) },
                { EAetheryteLocation.MareLamentorumSinusLacrimarum, new(-566.2471f, 134.66089f, 650.6294f) },
                { EAetheryteLocation.MareLamentorumBestwaysBurrow, new(-0.015319824f, -128.83197f, -512.0165f) },
                { EAetheryteLocation.ElpisAnagnorisis, new(159.96033f, 11.703674f, 126.878784f) },
                { EAetheryteLocation.ElpisTwelveWonders, new(-633.7225f, -19.821533f, 542.56494f) },
                { EAetheryteLocation.ElpisPoietenOikos, new(-529.9001f, 161.24207f, -222.2782f) },
                { EAetheryteLocation.UltimaThuleReahTahra, new(-544.152f, 74.32666f, 269.6421f) },
                { EAetheryteLocation.UltimaThuleAbodeOfTheEa, new(64.286255f, 272.48022f, -657.49603f) },
                { EAetheryteLocation.UltimaThuleBaseOmicron, new(489.2804f, 437.5829f, 333.63843f) },

                { EAetheryteLocation.Tuliyollal, new(-24.093994f, 0.77819824f, 7.583679f) },
                { EAetheryteLocation.TuliyollalDirigibleLanding, new(-413.68738f, 2.9754639f, -45.975464f) },
                { EAetheryteLocation.TuliyollalTheResplendentQuarter, new(-187.1214f, 39.93274f, 6.088318f) },
                { EAetheryteLocation.TuliyollalTheForardCabins, new(-149.73682f, -15.030151f, 198.90125f) },
                { EAetheryteLocation.TuliyollalBaysideBevyMarketplace, new(-14.999634f, -10.025269f, 135.57642f) },
                { EAetheryteLocation.TuliyollalVollokShoonsa, new(-99.13794f, 100.72473f, -222.03406f) },
                { EAetheryteLocation.TuliyollalWachumeqimeqi, new(166.27747f, -17.990417f, 38.742676f) },
                { EAetheryteLocation.TuliyollalBrightploomPost, new(71.7937f, 47.074097f, -333.21124f) },

                { EAetheryteLocation.SolutionNine, new(-0.015319824f, 8.987488f, -0.015319824f) },
                { EAetheryteLocation.SolutionNineInformationCenter, new(-30.441833f, -6.0579224f, 209.3385f) },
                { EAetheryteLocation.SolutionNineTrueVue, new(382.6809f, 59.983154f, 76.67651f) },
                { EAetheryteLocation.SolutionNineNeonStein, new(258.28943f, 50.736206f, 148.72961f) },
                { EAetheryteLocation.SolutionNineTheArcadion, new(374.77686f, 60.01367f, 325.67322f) },
                { EAetheryteLocation.SolutionNineResolution, new(-32.059265f, 38.04065f, -345.2354f) },
                { EAetheryteLocation.SolutionNineNexusArcade, new(-160.05188f, -0.015319824f, 21.591492f) },
                { EAetheryteLocation.SolutionNineResidentialSector, new(-378.13385f, 13.992493f, 136.49194f) },

                { EAetheryteLocation.UrqopachaWachunpelo, new(332.96704f, -160.11298f, -416.22034f) },
                { EAetheryteLocation.UrqopachaWorlarsEcho, new(465.62903f, 114.94617f, 634.9126f) },
                { EAetheryteLocation.KozamaukaOkHanu, new(-169.51251f, 6.576599f, -479.42322f) },
                { EAetheryteLocation.KozamaukaManyFires, new(541.16125f, 117.41809f, 203.60107f) },
                { EAetheryteLocation.KozamaukaEarthenshire, new(-477.53113f, 124.04053f, 311.32983f) },
                { EAetheryteLocation.KozamaukaDockPoga, new(787.59436f, 14.175598f, -236.22491f) },
                { EAetheryteLocation.YakTelIqBraax, new(-397.05505f, 23.5141f, -431.93713f) },
                { EAetheryteLocation.YakTelMamook, new(721.40076f, -132.31104f, 526.1769f) },
                { EAetheryteLocation.ShaaloaniHhusatahwi, new(386.40417f, -0.19836426f, 467.61267f) },
                { EAetheryteLocation.ShaaloaniShesheneweziSprings, new(-291.70673f, 19.08899f, -114.54956f) },
                { EAetheryteLocation.ShaaloaniMehwahhetsoan, new(311.36023f, -14.175659f, -567.74243f) },
                { EAetheryteLocation.HeritageFoundYyasulaniStation, new(514.6105f, 145.86096f, 207.56836f) },
                { EAetheryteLocation.HeritageFoundTheOutskirts, new(-223.0412f, 31.937134f, -584.03906f) },
                { EAetheryteLocation.HeritageFoundElectropeStrike, new(-219.53156f, 32.913696f, 120.77515f) },
                { EAetheryteLocation.LivingMemoryLeynodeMnemo, new(-0.22894287f, 57.175537f, 796.9634f) },
                { EAetheryteLocation.LivingMemoryLeynodePyro, new(657.98413f, 28.976807f, -284.01617f) },
                { EAetheryteLocation.LivingMemoryLeynodeAero, new(-255.26825f, 59.433838f, -397.6654f) },
            }
            .AsReadOnly();

    /// <summary>
    /// Airship landings are special as they're one-way only (except for Radz-at-Han, which is a normal aetheryte).
    /// </summary>
    private ReadOnlyDictionary<EAetheryteLocation, Vector3> AirshipLandingLocations { get; } =
        new Dictionary<EAetheryteLocation, Vector3>
        {
            { EAetheryteLocation.LimsaAirship, new(-19.44352f, 91.99999f, -9.892939f) },
            { EAetheryteLocation.GridaniaAirship, new(24.86354f, -19.000002f, 96f) },
            { EAetheryteLocation.UldahAirship, new(-16.954851f, 82.999985f, -9.421141f) },
            { EAetheryteLocation.KuganeAirship, new(-55.72525f, 79.10602f, 46.23109f) },
            { EAetheryteLocation.IshgardFirmament, new(9.92315f, -15.2f, 173.5059f) },
        }.AsReadOnly();

    public ReadOnlyDictionary<EAetheryteLocation, ushort> TerritoryIds { get; }
    public ReadOnlyDictionary<EAetheryteLocation, ushort> AethernetGroups { get; }
    private IReadOnlyList<ushort> TownTerritoryIds { get; set; }

    public float CalculateDistance(Vector3 fromPosition, ushort fromTerritoryType, EAetheryteLocation to)
    {
        if (!TerritoryIds.TryGetValue(to, out ushort toTerritoryType) || fromTerritoryType != toTerritoryType)
            return float.MaxValue;

        if (!Locations.TryGetValue(to, out Vector3 toPosition))
            return float.MaxValue;

        return (fromPosition - toPosition).Length();
    }

    public float CalculateAirshipLandingDistance(Vector3 fromPosition, ushort fromTerritoryType, EAetheryteLocation to)
    {
        if (!TerritoryIds.TryGetValue(to, out ushort toTerritoryType) || fromTerritoryType != toTerritoryType)
            return float.MaxValue;

        if (!AirshipLandingLocations.TryGetValue(to, out Vector3 toPosition))
            return float.MaxValue;

        return (fromPosition - toPosition).Length();
    }

    public bool IsCityAetheryte(EAetheryteLocation aetheryte)
    {
        if (aetheryte == EAetheryteLocation.IshgardFirmament)
            return true;

        var territoryId = TerritoryIds[aetheryte];
        return TownTerritoryIds.Contains(territoryId);
    }

    public bool IsAirshipLanding(EAetheryteLocation aetheryte) => AirshipLandingLocations.ContainsKey(aetheryte);

    public bool IsGoldSaucerAetheryte(EAetheryteLocation aetheryte) => TerritoryIds[aetheryte] is 144 or 388;
}
