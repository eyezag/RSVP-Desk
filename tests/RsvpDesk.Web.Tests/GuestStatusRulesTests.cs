using RsvpDesk.Engine;
using RsvpDesk.Web.Conversations;
using RsvpDesk.Web.Data;

namespace RsvpDesk.Web.Tests;

public sealed class GuestStatusRulesTests
{
    [Theory]
    [InlineData(Step.NotInvited, GuestStatus.Invited, false, GuestStatus.NotInvited)]
    [InlineData(Step.Invited, GuestStatus.NotInvited, false, GuestStatus.Invited)]
    [InlineData(Step.Invited, GuestStatus.Invited, false, GuestStatus.Invited)]
    [InlineData(Step.Invited, GuestStatus.Invited, true, GuestStatus.InChat)]
    [InlineData(Step.Invited, GuestStatus.Attending, true, GuestStatus.InChat)]
    [InlineData(Step.Party, GuestStatus.Invited, true, GuestStatus.InChat)]
    [InlineData(Step.Note, GuestStatus.InChat, true, GuestStatus.InChat)]
    [InlineData(Step.Confirmed, GuestStatus.InChat, true, GuestStatus.Attending)]
    [InlineData(Step.Declined, GuestStatus.Invited, true, GuestStatus.Declined)]
    [InlineData(Step.OptedOut, GuestStatus.InChat, true, GuestStatus.InChat)]
    [InlineData(Step.OptedOut, GuestStatus.Attending, true, GuestStatus.Attending)]
    public void Status_follows_the_conversation(Step step, GuestStatus current, bool replied, GuestStatus expected)
    {
        Assert.Equal(expected, GuestStatusRules.Next(step, current, replied));
    }
}
