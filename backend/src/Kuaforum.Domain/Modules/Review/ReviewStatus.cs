namespace Kuaforum.Domain.Modules.Review;

public enum ReviewModerationStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Hidden = 3,
}

public enum ReviewRejectionReason
{
    Profanity = 0,
    Spam = 1,
    OffTopic = 2,
    PersonalAttack = 3,
    Other = 99,
}
