using RsvpDesk.Engine;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Conversations;

public static class GuestStatusRules
{
    /// <summary>
    /// The cached guest status after a conversation step. <paramref name="guestReplied"/> is true when
    /// the change came from a message the guest sent. Opting out does not change the status: the
    /// dashboard shows opt-out separately, and the answers given before it are kept.
    /// </summary>
    public static GuestStatus Next(Step step, GuestStatus current, bool guestReplied) => step switch
    {
        Step.NotInvited => GuestStatus.NotInvited,
        Step.Invited when guestReplied => GuestStatus.InChat,
        Step.Invited => current is GuestStatus.NotInvited ? GuestStatus.Invited : current,
        Step.Party or Step.Diet or Step.DietOther or Step.Wheelchair or Step.WheelchairCount or Step.Note => GuestStatus.InChat,
        Step.Confirmed => GuestStatus.Attending,
        Step.Declined => GuestStatus.Declined,
        Step.OptedOut => current,
        _ => current,
    };
}
