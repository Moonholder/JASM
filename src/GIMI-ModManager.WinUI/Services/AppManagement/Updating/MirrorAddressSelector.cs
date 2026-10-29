using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Services.Network.Models;

namespace GIMI_ModManager.WinUI.Services.AppManagement.Updating;

public static class MirrorAddressSelector
{
    private const string TestUrl = "https://raw.githubusercontent.com/Moonholder/JASM/main/README.md";

    /// <summary>
    /// Obtains available mirrors sorted by latency (fastest first), delegating to IMirrorRacingService.
    /// </summary>
    public static async Task<List<MirrorInfo>> GetAvailableMirrorsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var racingService = App.GetService<IMirrorRacingService>();
            if (racingService != null)
            {
                var ranked = await racingService.GetRankedMirrorsAsync(TestUrl, cancellationToken: cancellationToken).ConfigureAwait(false);
                return ranked.Select(m => new MirrorInfo(m.Address, m.NodeName, m.SupportsApiForward)).ToList();
            }
        }
        catch
        {
            // Fallback to safe Direct mirror if resolution fails
        }

        return [new MirrorInfo("", "GitHub Direct", false)];
    }

    public class MirrorInfo(string address, string nodeName, bool supportsApiForward = true)
    {
        public string Address { get; } = address;
        public string NodeName { get; } = nodeName;
        public bool SupportsApiForward { get; } = supportsApiForward;
    }
}