using System;
using System.Collections.Generic;
using System.Linq;
using DHCPSwitches.Models;
using greg.Sdk.Services;

namespace DHCPSwitches;

public static class LeaseStore
{
    private const string ModName = "gregIPAM";
    private const string LeaseKey = "leases";
    private const string ReservationKey = "reservations";

    private static readonly List<IpLease> Leases = GregPersistenceService.Load(ModName, LeaseKey, new List<IpLease>());
    private static readonly List<DhcpReservation> Reservations = GregPersistenceService.Load(ModName, ReservationKey, new List<DhcpReservation>());

    public static IpLease CreateLease(string serverId, string ip, int customerId, int appId, string subnetId, LeaseSource source)
    {
        var existing = GetLeaseByServerId(serverId);
        if (existing != null)
        {
            Leases.Remove(existing);
        }

        var lease = new IpLease
        {
            LeaseId = Guid.NewGuid().ToString("N"),
            ServerId = serverId,
            Ip = ip,
            CustomerId = customerId,
            AppId = appId,
            SubnetId = subnetId,
            AssignedAt = DateTime.UtcNow.ToString("o"),
            Source = source,
        };

        Leases.Add(lease);
        Save();
        return lease;
    }

    public static void RevokeLease(string leaseId)
    {
        var lease = Leases.FirstOrDefault(x => x.LeaseId == leaseId);
        if (lease == null)
        {
            return;
        }

        Leases.Remove(lease);
        Save();
    }

    public static IpLease GetLeaseByServerId(string serverId) => Leases.FirstOrDefault(x => x.ServerId == serverId);

    public static IpLease GetLeaseByIp(string ip) => Leases.FirstOrDefault(x => x.Ip == ip);

    public static List<IpLease> GetAllLeases() => new(Leases);

    public static void AddReservation(DhcpReservation reservation)
    {
        if (reservation == null)
        {
            return;
        }

        Reservations.RemoveAll(x => x.ServerId == reservation.ServerId || x.Ip == reservation.Ip);
        Reservations.Add(reservation);
        Save();
    }

    public static DhcpReservation GetReservation(string serverId)
    {
        return Reservations.FirstOrDefault(x => x.ServerId == serverId);
    }

    private static void Save()
    {
        GregPersistenceService.Save(ModName, LeaseKey, Leases);
        GregPersistenceService.Save(ModName, ReservationKey, Reservations);
    }
}
