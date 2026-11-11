using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using RsvpDesk.Engine;
using RsvpDesk.Web.Channels;
using RsvpDesk.Web.Data;
using RsvpDesk.Web.WhatsApp;

namespace RsvpDesk.Web.Tests;

public sealed class WebhookSignatureTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"object":"whatsapp_business_account","entry":[]}""");

    [Fact]
    public void Accepts_the_HMAC_of_the_raw_body()
    {
        Assert.True(WebhookSignature.IsValid(Body, WebhookSignature.Compute(Body, "secret"), "secret"));
        Assert.True(WebhookSignature.IsValid(Body, WebhookSignature.Compute(Body, "secret").ToUpperInvariant().Replace("SHA256=", "sha256="), "secret"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256=")]
    [InlineData("sha256=not-hex")]
    [InlineData("sha1=0123456789abcdef")]
    [InlineData("sha256=0000000000000000000000000000000000000000000000000000000000000000")]
    public void Rejects_missing_malformed_or_wrong_signatures(string? header)
    {
        Assert.False(WebhookSignature.IsValid(Body, header, "secret"));
    }

    [Fact]
    public void Rejects_a_signature_made_with_another_secret_or_for_another_body()
    {
        Assert.False(WebhookSignature.IsValid(Body, WebhookSignature.Compute(Body, "other"), "secret"));
        Assert.False(WebhookSignature.IsValid(Body, WebhookSignature.Compute(Encoding.UTF8.GetBytes("{}"), "secret"), "secret"));
    }

    [Fact]
    public void Verify_tokens_compare_exactly()
    {
        Assert.True(WebhookSignature.TokensMatch("abc", "abc"));
        Assert.False(WebhookSignature.TokensMatch("abd", "abc"));
        Assert.False(WebhookSignature.TokensMatch(null, "abc"));
        Assert.False(WebhookSignature.TokensMatch("", "abc"));
    }
}

public sealed class WhatsAppNormaliseTests
{
    private readonly WhatsAppCloudChannel _channel = WhatsAppTestOptions.Channel(new FakeGraphApi());

    private ChannelEvent Single(string fixture) => _channel.Normalise(Assert.Single(WebhookSplitter.Split(Fixtures.Read(fixture))).Json).Single();

    [Fact]
    public void Text_message()
    {
        var inbound = Assert.IsType<NormalisedInbound>(Single("text.json"));

        Assert.Equal(new GuestAddress(null, "+27000000001", "ZA.000000000000000001"), inbound.From);
        Assert.Equal("wamid.FIXTURE.TEXT", inbound.MessageId);
        Assert.Equal("yes", inbound.Text);
        Assert.Null(inbound.ReplyId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1764950400), inbound.SentAt);
    }

    [Theory]
    [InlineData("button_reply.json", "attend.yes", "Yes, I'll be there")]
    [InlineData("list_reply.json", "diet.vegetarian", "Vegetarian")]
    [InlineData("template_quick_reply.json", "attend.yes", "Yes")]
    public void Taps_carry_the_option_id_and_title(string fixture, string replyId, string title)
    {
        var inbound = Assert.IsType<NormalisedInbound>(Single(fixture));

        Assert.Equal(replyId, inbound.ReplyId);
        Assert.Equal(title, inbound.Text);
    }

    [Fact]
    public void A_user_without_a_phone_number_is_identified_by_user_id()
    {
        var inbound = Assert.IsType<NormalisedInbound>(Single("username_only.json"));

        Assert.Null(inbound.From.PhoneE164);
        Assert.Equal("ZA.000000000000000001", inbound.From.ProviderUserId);
    }

    [Fact]
    public void Reactions_are_ignored()
    {
        Assert.IsType<IgnoredEvent>(Single("reaction.json"));
    }

    [Theory]
    [InlineData("status_sent.json", DeliveryStatus.Sent)]
    [InlineData("status_delivered.json", DeliveryStatus.Delivered)]
    [InlineData("status_read.json", DeliveryStatus.Read)]
    [InlineData("status_failed.json", DeliveryStatus.Failed)]
    public void Statuses(string fixture, DeliveryStatus expected)
    {
        var update = Assert.IsType<DeliveryUpdate>(Single(fixture));

        Assert.Equal("wamid.FIXTURE.OUT.1", update.ProviderMessageId);
        Assert.Equal(expected, update.Status);
        if (expected == DeliveryStatus.Failed)
        {
            Assert.Equal("131026: Message undeliverable", update.Error);
        }
    }

    [Fact]
    public void A_batch_is_split_into_one_item_per_message_and_status()
    {
        var items = WebhookSplitter.Split(Fixtures.Read("batch.json"));

        Assert.Equal(["wamid.FIXTURE.BATCH.1", "wamid.FIXTURE.BATCH.2", "status:wamid.FIXTURE.OUT.1:delivered"], items.Select(i => i.ExternalId));
    }

    [Theory]
    [InlineData("""{"object":"page","entry":[]}""")]
    [InlineData("""{"object":"whatsapp_business_account","entry":[{"changes":[{"field":"message_template_status_update","value":{}}]}]}""")]
    public void Other_objects_and_fields_produce_nothing(string body)
    {
        Assert.Empty(WebhookSplitter.Split(body));
    }

    [Fact]
    public void Unparseable_JSON_is_kept_as_one_item_so_the_error_is_visible()
    {
        var item = Assert.Single(WebhookSplitter.Split("not json"));

        Assert.StartsWith("unparsed:", item.ExternalId);
        Assert.Throws<FormatException>(() => _channel.Normalise(item.Json));
    }
}

public sealed class WhatsAppPayloadTests
{
    private static readonly EventDetails Ev = new("Year-End Function 2026", new DateTimeOffset(2026, 12, 5, 18, 0, 0, TimeSpan.FromHours(2)), "The Jacaranda Hall", 4);
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly MessageRenderer Renderer = new();
    private static readonly ConversationEngine Engine = new();

    private static JsonObject Build(OutboundMessage message, Step step = Step.Party, DateTimeOffset? lastInbound = null) =>
        WhatsAppPayloads.Build(
            new OutboundEnvelope(1, "+27000000001", null, Renderer.Render(message), new OutboundContext(Ev, step, lastInbound ?? Now.AddMinutes(-1))),
            WhatsAppTestOptions.Create(),
            Renderer,
            Now);

    private static ConversationState At(Step step, int partySize = 2) => ConversationState.New with
    {
        Step = step,
        Answers = RsvpAnswers.Empty with { Attending = true, PartySize = partySize },
    };

    [Fact]
    public void Invitation_is_the_approved_template_with_named_parameters()
    {
        var p = Build(Engine.Invite(ConversationState.New, Ev).Messages[0], Step.Invited, lastInbound: null);

        Assert.Equal("template", p["type"]!.GetValue<string>());
        Assert.Equal("+27000000001", p["to"]!.GetValue<string>());
        Assert.Equal("whatsapp", p["messaging_product"]!.GetValue<string>());
        var template = p["template"]!;
        Assert.Equal("rsvp_invitation", template["name"]!.GetValue<string>());
        Assert.Equal("en", template["language"]!["code"]!.GetValue<string>());
        var parameters = template["components"]![0]!["parameters"]!.AsArray().Select(x => (x!["parameter_name"]!.GetValue<string>(), x["text"]!.GetValue<string>()));
        Assert.Equal([("event_name", "Year-End Function 2026"), ("event_date", "Saturday 5 December 2026 at 18:00"), ("venue", "The Jacaranda Hall")], parameters);
    }

    [Fact]
    public void Yes_no_questions_are_reply_buttons()
    {
        var p = Build(Engine.Prompt(At(Step.Wheelchair), Ev));

        Assert.Equal("button", p["interactive"]!["type"]!.GetValue<string>());
        var buttons = p["interactive"]!["action"]!["buttons"]!.AsArray();
        Assert.Equal(["wheelchair.yes", "wheelchair.no"], buttons.Select(b => b!["reply"]!["id"]!.GetValue<string>()));
        Assert.All(buttons, b => Assert.Equal("reply", b!["type"]!.GetValue<string>()));
    }

    [Fact]
    public void Dietary_options_are_a_list_with_one_titled_section()
    {
        var p = Build(Engine.Prompt(At(Step.Diet), Ev));

        var interactive = p["interactive"]!;
        Assert.Equal("list", interactive["type"]!.GetValue<string>());
        Assert.Equal("Choose", interactive["action"]!["button"]!.GetValue<string>());
        var section = Assert.Single(interactive["action"]!["sections"]!.AsArray())!;
        Assert.Equal("Options", section["title"]!.GetValue<string>());
        Assert.Equal(6, section["rows"]!.AsArray().Count);
        Assert.All(section["rows"]!.AsArray(), r => Assert.True(r!["title"]!.GetValue<string>().Length <= WhatsAppPayloads.MaxListRowTitle));
    }

    [Theory]
    [InlineData(3, "button")]
    [InlineData(4, "list")]
    [InlineData(10, "list")]
    public void Party_size_uses_buttons_up_to_three_and_a_list_up_to_ten(int partyMax, string kind)
    {
        var p = Build(Engine.Prompt(At(Step.Party), Ev with { PartyMax = partyMax }));

        Assert.Equal(kind, p["interactive"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Free_text_questions_are_plain_text()
    {
        var p = Build(Engine.Prompt(At(Step.DietOther), Ev));

        Assert.Equal("text", p["type"]!.GetValue<string>());
        Assert.Equal("Please type the requirement, for example nut allergy.", p["text"]!["body"]!.GetValue<string>());
    }

    [Fact]
    public void Reminders_are_templates_outside_the_window_and_normal_messages_inside_it()
    {
        var notReplied = Engine.Remind(At(Step.Invited), Ev).Messages[0];
        var unfinished = Engine.Remind(At(Step.Diet), Ev).Messages[0];

        Assert.Equal("rsvp_reminder", Build(notReplied, Step.Invited, Now.AddDays(-3))["template"]!["name"]!.GetValue<string>());
        Assert.Equal("rsvp_reminder_unfinished", Build(unfinished, Step.Diet, Now.AddDays(-3))["template"]!["name"]!.GetValue<string>());
        Assert.Equal("interactive", Build(unfinished, Step.Diet, Now.AddHours(-2))["type"]!.GetValue<string>());
    }

    [Fact]
    public void Over_long_text_is_cut_to_the_limit()
    {
        var long1500 = new RenderedMessage(MessagePurpose.Reply, new string('a', 1500), [new RenderedChoice("x", "X")]);
        var p = WhatsAppPayloads.Build(new OutboundEnvelope(1, "+27000000001", null, long1500, new OutboundContext(Ev, Step.Party, Now)), WhatsAppTestOptions.Create(), Renderer, Now);

        Assert.Equal(WhatsAppPayloads.MaxButtonBody, p["interactive"]!["body"]!["text"]!.GetValue<string>().Length);
    }
}

public sealed class WhatsAppSendTests
{
    private static OutboundEnvelope Envelope() => new(
        7,
        "+27000000001",
        null,
        new RenderedMessage(MessagePurpose.Reply, "Hello", []),
        new OutboundContext(new EventDetails("E", DateTimeOffset.UtcNow.AddDays(9), "V", 4), Step.Party, DateTimeOffset.UtcNow));

    [Fact]
    public async Task Posts_to_the_phone_number_messages_endpoint_with_the_token()
    {
        var graph = new FakeGraphApi();

        var result = await WhatsAppTestOptions.Channel(graph).SendAsync(Envelope(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("wamid.TEST.OUT.1", result.ProviderMessageId);
        var (url, auth, _) = Assert.Single(graph.Requests);
        Assert.Equal("https://graph.facebook.com/v27.0/100000000000001/messages", url);
        Assert.Equal("Bearer test-access-token", auth);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, 130429)]
    [InlineData(HttpStatusCode.BadRequest, 131056)]
    [InlineData(HttpStatusCode.InternalServerError, 131000)]
    public async Task Retries_rate_limits_and_temporary_errors(HttpStatusCode status, int code)
    {
        var graph = new FakeGraphApi();
        graph.Enqueue(status, FakeGraphApi.Error(code, "Try again"));

        var result = await WhatsAppTestOptions.Channel(graph).SendAsync(Envelope(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, graph.Requests.Count);
    }

    [Theory]
    [InlineData(131026, "Message undeliverable")]
    [InlineData(131047, "Re-engagement message")]
    [InlineData(132001, "Template name does not exist in the translation")]
    public async Task Does_not_retry_permanent_errors(int code, string message)
    {
        var graph = new FakeGraphApi();
        graph.Enqueue(HttpStatusCode.BadRequest, FakeGraphApi.Error(code, message));

        var result = await WhatsAppTestOptions.Channel(graph).SendAsync(Envelope(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.StartsWith($"{code}: ", result.Error);
        Assert.Single(graph.Requests);
    }

    [Fact]
    public async Task Gives_up_after_the_maximum_attempts()
    {
        var graph = new FakeGraphApi();
        for (var i = 0; i < 5; i++)
        {
            graph.Enqueue(HttpStatusCode.ServiceUnavailable, "{}");
        }

        var result = await WhatsAppTestOptions.Channel(graph).SendAsync(Envelope(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(3, graph.Requests.Count);
    }
}
