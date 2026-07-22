using DV.UI;

namespace ModProfiles.UI;

public class ProfileTooltipText : UIElementTooltipCustomText
{
    public string Text = "";

    public override string GetText() => Text;
}
