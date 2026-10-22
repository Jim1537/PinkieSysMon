using System.Windows.Forms;
using PinkieSysMon.Editor;

namespace PinkieSysMon.RegressionTests;

internal static class Issue95ProtocolChoiceTests
{
    public static void CatalogChoicesAndDraftAdmission()
    {
        foreach (var family in UsbDisplayCatalog.Families)
        {
            var choices = UsbDisplayCatalog.ProtocolChoices(family);
            Check(choices.Select(choice => choice.Id).Distinct().Count() == choices.Count &&
                choices.Where(choice => choice.IsImplemented).Select(choice => choice.Id).ToHashSet().SetEquals(
                    [OutputTargetContract.LyBulkTransport, OutputTargetContract.EncryptedJpegTransport]),
                "Every eligible listed interface retains both implemented protocol choices independently of its family hint.");
            Check(choices.Where(choice => !choice.IsImplemented).All(choice =>
                choice.Id == family.UnimplementedProtocol && choice.Label.EndsWith("Not implemented yet")),
                "Only the family's documented missing protocol appears, explicitly unavailable.");
            var candidate = Candidate(family);
            var draft = new CatalogUsbCandidateDraft(candidate, "Unknown user-selected commercial name",
                choices.Where(choice => choice.IsImplemented).Select(choice => choice.Id)) { Width = 800, Height = 600 };
            Check(draft.Protocol == family.CandidateTransport,
                "Only a justified implemented hint may be preselected; an unhinted family remains unselected.");
            foreach (var choice in choices)
            {
                draft.Protocol = choice.Id;
                Check(draft.CanTest == choice.IsImplemented, "Missing implementations cannot become testable or dispatchable.");
            }
        }
        var hid = UsbDisplayCatalog.Find(new(0x0416, 0x5302))!;
        Check(UsbDisplayCatalog.ProtocolChoices(hid).Single(choice => !choice.IsImplemented).Id == "hid-type2",
            "The known HID Type 2 family must not be mistaken for the LY bulk implementation.");
    }

    public static void CandidateRefreshPreservesExplicitChoice()
    {
        var family = UsbDisplayCatalog.Find(new(0x0416, 0x5408))!;
        var candidate = Candidate(family);
        var device = Descriptor(candidate);
        var config = new AppConfig(); var model = new DeviceSettingsModel(config); model.Refresh([device]);
        var target = model.Entries.Single().Target;
        Check(target.Transport == OutputTargetContract.LyBulkTransport && target.Width == 0 && target.Height == 0,
            "The passive hint may suggest a protocol but must never infer screen geometry.");
        target.Name = "Custom label"; target.Transport = OutputTargetContract.EncryptedJpegTransport;
        target.Width = 800; target.Height = 600; model.MarkChanged(target);
        model.Refresh([]); model.Refresh([device]);
        var restored = model.Entries.Single();
        Check(ReferenceEquals(restored.Target, target) && target.Name == "Custom label" &&
            model.CandidateDraft(target)!.Protocol == OutputTargetContract.EncryptedJpegTransport &&
            target.Width == 800 && target.Height == 600 && !model.IsDirty && config.Outputs.Targets.Count == 1,
            "A passive replug preserves explicit draft choices without overwriting them from hints or saving an Output.");
    }

    public static void RadioAvailabilityAndReconnect()
    {
        var candidate = Candidate(UsbDisplayCatalog.Find(new(0x0416, 0x5302))!);
        var model = new DeviceSettingsModel(new AppConfig()); model.Refresh([Descriptor(candidate)]);
        var entry = model.Entries.Single(); var target = entry.Target;
        using var page = new DeviceSettingsPage(target, ["Default"], () => model.MarkChanged(target), _ => false,
            () => throw new InvalidOperationException("Unexpected Save."), () => { }, candidate: model.CandidateDraft(target));
        page.UpdateState(entry, false);
        var group = page.Controls.Find("DeviceProtocol", true).Single();
        var radios = group.Controls.OfType<RadioButton>().ToArray();
        Check(radios.Length == 3 && radios.Count(radio => radio.Enabled) == 2 && radios.All(radio => !radio.Checked),
            "An unhinted candidate shows two usable choices and one disabled known protocol without arbitrary preselection.");
        var missing = radios.Single(radio => !radio.Enabled);
        Check(missing.Text.EndsWith("Not implemented yet"), "Unavailable radio label must explicitly name missing implementation.");
        radios.Single(radio => Equals(radio.Tag, OutputTargetContract.EncryptedJpegTransport)).Checked = true;
        Check(target.Transport == OutputTargetContract.EncryptedJpegTransport &&
            model.CandidateDraft(target)!.Protocol == target.Transport && target.EncryptedJpeg is not null,
            "Selecting a usable radio changes the same candidate wire snapshot and protocol-specific defaults.");
        page.UpdateState(entry with { Status = PhysicalDeviceInventoryStatus.Disconnected }, false);
        Check(radios.All(radio => !radio.Enabled), "Disconnect disables all candidate protocol choices.");
        page.UpdateState(entry, false);
        Check(!missing.Enabled && radios.Count(radio => radio.Enabled) == 2 && radios.Count(radio => radio.Checked) == 1,
            "Reconnect restores usable choices while preserving the explicit selection and disabled missing implementation.");
        Check(!page.Controls.Find("DeviceTest", true).Single().Enabled && !page.Controls.Find("AddDevice", true).Single().Enabled,
            "A page without a Runtime Test callback cannot dispatch or save a candidate.");
        using var informational = new RecognizedDeviceSettingsPage();
        informational.UpdateState(new(new UsbDisplayObservation("synthetic", candidate.Family,
            UsbDisplayObservationStatus.Recognized, 1, ["HIDUSB"], [], []), true));
        var details = ((Label)informational.Controls.Find("RecognizedDeviceDetails", true).Single()).Text;
        Check(details.Contains("No usable WinUSB driver was observed") && details.Contains(missing.Text) &&
            !details.Contains("synthetic-private-interface"),
            "Recognition-only cards explain the actual driver prerequisite and the same missing protocol without exposing native paths.");
    }

    public static void LiveActionStates()
    {
        var candidate = Candidate(UsbDisplayCatalog.Find(new(0x0416, 0x5408))!);
        var model = new DeviceSettingsModel(new AppConfig()); model.Refresh([Descriptor(candidate)]);
        var entry = model.Entries.Single(); var draft = model.CandidateDraft(entry.Target)!;
        using var page = new DeviceSettingsPage(entry.Target, ["Default"], () => model.MarkChanged(entry.Target), _ => false,
            () => throw new InvalidOperationException("Unexpected Save."), () => { }, candidate: draft, test: () => Task.CompletedTask);
        var actions = new[] { "DeviceTest", "AddDevice", "ForgetDevice", "CancelDevice" }
            .Select(name => (Button)page.Controls.Find(name, true).Single()).ToArray();
        Check(actions.Select(button => button.Text).SequenceEqual(["Test", "Save Configuration", "Forget", "Cancel"]) &&
            actions.All(button => button.Parent == actions[0].Parent), "All four candidate actions stay in the same action row.");
        page.UpdateState(entry, false);
        Check(!actions[0].Enabled && actions[3].Enabled, "Invalid empty geometry cannot dispatch Test; Cancel remains available.");
        ((NumericUpDown)page.Controls.Find("DeviceWidth", true).Single()).Value = 320;
        ((NumericUpDown)page.Controls.Find("DeviceHeight", true).Single()).Value = 240;
        page.UpdateState(entry, false);
        Check(actions[0].Enabled && !actions[1].Enabled && !actions[2].Enabled,
            "Valid candidate can Test; unimplemented Save/Forget transactions cannot fall through to legacy dialog persistence.");
        var intent = draft.BeginTest(new([candidate], []), new HashSet<string>()); draft.MarkDispatchAttempted(intent);
        page.UpdateState(entry, false, true);
        Check(!actions[0].Enabled && !page.Controls.Find("DeviceFields", true).Single().Enabled && actions[3].Enabled,
            "In-flight Test locks wire inputs and repeat dispatch while keeping Cancel available.");
        draft.RecordTechnicalOutcome(intent, CatalogUsbTestTechnicalOutcome.SucceededAndReleased);
        draft.ConfirmVisibleChangingSeconds(intent, true); page.UpdateState(entry, false);
        Check(draft.CanSaveConfiguration && actions[0].Enabled && !actions[1].Enabled && !actions[2].Enabled,
            "Positive confirmation changes draft eligibility only; staged persistence remains deliberately unavailable.");
    }

    private static CatalogUsbInterfaceCandidate Candidate(UsbDisplayFamily family) => new(
        "usb-interface:" + new string('a', 64), family, "synthetic-private-interface", "usb-pnp:" + new string('b', 64), "Unknown display");
    private static OutputDeviceDescriptor Descriptor(CatalogUsbInterfaceCandidate candidate) => new(
        candidate.Id, candidate.SuggestedTransport ?? "", candidate.DisplayName, candidate.InterfacePath)
        { Candidate = candidate, PnpNodeKey = candidate.PnpNodeKey };
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
