using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Geography;

public readonly record struct Coordinate
{
    public double Latitude { get; }
    public double Longitude { get; }

    private Coordinate(double lat, double lng)
    {
        Latitude = lat;
        Longitude = lng;
    }

    public static Result<Coordinate> TryCreate(double lat, double lng)
    {
        if (lat is < -90 or > 90)
            return Result.Failure<Coordinate>(Error.Validation("Latitude must be between -90 and 90."));
        if (lng is < -180 or > 180)
            return Result.Failure<Coordinate>(Error.Validation("Longitude must be between -180 and 180."));
        return new Coordinate(lat, lng);
    }

    public static Coordinate UnsafeCreate(double lat, double lng) => new(lat, lng);
}
