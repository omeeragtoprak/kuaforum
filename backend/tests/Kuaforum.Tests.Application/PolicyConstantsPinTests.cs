using Kuaforum.Application.Modules.Booking;
using Kuaforum.Domain.Modules.Barber;

namespace Kuaforum.Tests.Application;

/// <summary>
/// RFC-013 review M4: <see cref="BookingPolicy.MaxPendingPerCustomerCeiling"/> (Domain, per-shop
/// üst sınır) ve <see cref="InitiateBookingCommandHandler.MaxActivePendingBookingsPerCustomer"/>
/// (Application, shop'tan bağımsız GLOBAL taban) BİLEREK iki ayrı sabit — ama aynı değeri
/// (3) taşımaları gerekiyor: aksi halde bir owner, per-shop <c>MaxPendingPerCustomer</c>'ı
/// domain'in izin verdiği tavana kadar yükseltip global tabanı SESSİZCE aşabilir. Bu test
/// yalnızca "ikisi hâlâ eşit mi" sorusunu pinler — biri değişip diğeri unutulursa burada kırılır.
/// </summary>
public class PolicyConstantsPinTests
{
    [Fact]
    public void PerShopCeiling_MatchesGlobalPendingCap()
    {
        Assert.Equal(InitiateBookingCommandHandler.MaxActivePendingBookingsPerCustomer, BookingPolicy.MaxPendingPerCustomerCeiling);
    }
}
