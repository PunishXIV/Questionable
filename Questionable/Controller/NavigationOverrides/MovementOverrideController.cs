using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace Questionable.Controller.NavigationOverrides;

internal sealed class MovementOverrideController
{
    private static readonly List<IBlacklistedLocation> BlacklistedLocations =
    [
        new BlacklistedArea(1191, new(-223.0412f, 31.937134f, -584.03906f), 5f, 7.75f),

        // limsa, aftcastle to Baderon
        new BlacklistedPoint(128, new(2f, 40.25f, 36.5f), new(0.25f, 40.25f, 36.5f)),

        // New Gridania, Carline Canopy stairs
        new BlacklistedPoint(132, new(29, -8, 120.5f), new(28.265165f, -8.000001f, 120.149734f)),
        new BlacklistedPoint(132, new(28.25f, -8, 125), new(27.372725f, -8.200001f, 125.55859f)),
        new BlacklistedPoint(132, new(32.25f, -8, 126.5f), new(32.022232f, -8.200011f, 126.86095f)),

        // lotus stand
        new BlacklistedPoint(205, new(26.75f, 0.5f, 20.75f), new(27.179117f, 0.26728272f, 19.714373f)),

        // ul'dah lamp near adventurers' guild
        new BlacklistedPoint(130, new(59.5f, 4.25f, -118f), new(60.551353f, 4f, -119.76446f)),

        // eastern thanalan
        new BlacklistedPoint(145, new(-139.75f, -32.25f, 75.25f), new(-139.57748f, -33.785175f, 77.87906f)),

        // southern thanalan
        new BlacklistedPoint(146, new(-201.75f, 10.5f, -265.5f), new(-203.75235f, 10.130764f, -265.15314f)),

        // lower la noscea - Moraby Drydocks aetheryte
        new BlacklistedArea(135, new(156.11499f, 15.518433f, 673.21277f), 0.5f, 5f),

        // upper la noscea
        new BlacklistedPoint(139, new(366, -2.5f, 95.5f), new(362.65973f, -3.4f, 96.6896f), 2),

        // coerthas central highlands
        new BlacklistedPoint(155, new(-478.75f, 149.25f, -305.75f), new(-476.1802f, 149.06573f, -304.7811f)),

        // rising stones, plant boxes
        new BlacklistedPoint(351, new(3.25f, 0.75f, 8.5f), new(4f, 0f, 9.5f)),

        // ishgard, clutter
        new BlacklistedPoint(418, new(-136.75f, 2.75f, 9), new(-138.66408f, 2.0333426f, 8.860787f), 1f),

        // southern sea of clouds, random rock
        new BlacklistedPoint(401, new(-14.75f, -136.75f, 515.75f), new(-17.631899f, -137.39148f, 512.6676f), 2),

        // coerthas western highland, right before dusk vigil
        new BlacklistedPoint(397, new(-93.75f, 87.75f, -715.5f), new(-87.78183f, 87.188995f, -713.3343f), 2),

        // moghome, mogmug's trial
        new BlacklistedPoint(400, new(384, -74, 648.75f), new(386.0543f, -72.409454f, 652.0184f), 3),

        // leaving Idyllshire through the west gate attempts to run into this wall
        new BlacklistedPoint(399, new(-514.4851f, 149.63762f, -480.58087f), new(-528.78656f, 151.17374f, -473.07077f), 5, true),
        new BlacklistedPoint(399, new(-534.5f, 153, -476.75f), new(-528.78656f, 151.17374f, -473.07077f), 5, true),

        // Idyllshire: random rocks in the north, passable one way only
        new BlacklistedPoint(478, new(14.5f, 215.25f, -101.5f), new(18.133032f, 215.44998f, -107.83075f), 5),
        new BlacklistedPoint(478, new(11, 215.5f, -104.5f), new(18.133032f, 215.44998f, -107.83075f), 5),

        new BlacklistedPoint(1189, new(574f, -142.25f, 504.25f), new(574.44183f, -142.12766f, 507.60065f)),

        // kholusia, random rocks
        new BlacklistedPoint(814, new(-324, 348.75f, -181.75f), new(-322.75076f, 347.0529f, -177.69328f), 3),

        // labyrinthos, sharlayan hamlet aetheryte
        new BlacklistedPoint(956, new(6.25f, -27.75f, -41.5f), new(5.0831127f, -28.213453f, -42.239136f)),

        // yak t'el, rock near cenote jayunja
        new BlacklistedPoint(1189, new(-115.75f, -213.75f, 336.5f), new(-112.40265f, -215.01514f, 339.0067f), 2),

        // sheshenewezi springs aetheryte: a couple of barrel rings that get in the way if you go north
        new BlacklistedPoint(1190, new(-292.29004f, 18.598045f, -133.83907f), new(-288.20895f, 18.652182f, -132.67445f),
            4),

        // heritage found: yyupye's halo (farm, npc: Mahuwsa)
        new BlacklistedPoint(1191, new(-108f, 29.25f, -350.75f), new(-107.56289f, 29.008266f, -348.80087f)),
        new BlacklistedPoint(1191, new(-105.75f, 29.75f, -351f), new(-105.335304f, 29.017048f, -348.85077f)),

        // solution nine: walks behind the bar in front of the backrooms thing
        new BlacklistedPoint(1186, new(284.25f, 50.75f, 171.25f), new(284.25f, 50.75f, 166.25f)),
        new BlacklistedPoint(1186, new(283.75f, 50.75f, 167.25f), new(284.25f, 50.75f, 166.25f)),
        new BlacklistedPoint(1186, new(287.75f, 51.25f, 172f), new(288.875f, 50.75f, 166.25f)),
    ];

    private readonly IClientState _clientState;
    private readonly ILogger<MovementOverrideController> _logger;

    public MovementOverrideController(IClientState clientState, ILogger<MovementOverrideController> logger)
    {
        _clientState = clientState;
        _logger = logger;
    }

    /// <summary>
    /// Certain areas shouldn't have navmesh points in them, e.g. the aetheryte in HF Outskirts can't be
    /// walked on without jumping, but if you teleport to the wrong side you're fucked otherwise.
    /// </summary>
    /// <param name="navPoints">list of points to check</param>
    public (List<Vector3>, bool) AdjustPath(List<Vector3> navPoints)
    {
        foreach (var blacklistedArea in BlacklistedLocations)
        {
            if (_clientState.TerritoryType != blacklistedArea.TerritoryId)
                continue;

            for (int i = 0; i < navPoints.Count; ++i)
            {
                AlternateLocation? alternateLocation = blacklistedArea.AdjustPoint(navPoints[i]);

                if (alternateLocation != null)
                {
                    _logger.LogInformation("Fudging navmesh point from {Original} to {Replacement} in blacklisted area",
                        navPoints[i].ToString("G", CultureInfo.InvariantCulture),
                        alternateLocation);

                    navPoints[i] = alternateLocation.Point;
                    if (alternateLocation.RecalculateNavmesh)
                        return (navPoints.Take(i + 1).ToList(), true);
                }
            }
        }

        return (navPoints, false);
    }
}
