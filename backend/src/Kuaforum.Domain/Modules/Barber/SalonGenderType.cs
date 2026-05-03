namespace Kuaforum.Domain.Modules.Barber;

public enum SalonGenderType
{
    Unisex = 0,
    FemaleOnly = 1,
    MaleOnly = 2,
}

public enum BarberShopStatus
{
    Draft = 0,
    PendingKyc = 1,
    Active = 2,
    Suspended = 3,
    Closed = 4,
}

public enum KycStatus
{
    Pending = 0,
    UnderReview = 1,
    Approved = 2,
    Rejected = 3,
}

public enum ServiceGender
{
    Any = 0,
    Female = 1,
    Male = 2,
}

public enum DocumentType
{
    TaxLicense = 0,
    SanitaryPermit = 1,
    MykCertificate = 2,
    IdentityCard = 3,
    Other = 99,
}
