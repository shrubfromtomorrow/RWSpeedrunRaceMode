using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Menu.Remix.MixedUI;

namespace SpeedrunRaceMode
{
    public class Options : OptionInterface
    {
        public readonly Configurable<KeyCode> mercyButton;
        public readonly Configurable<bool> LapMode;
        private UIelement[]? options;

        public Options()
        {
            mercyButton = config.Bind<KeyCode>("MercyButton", KeyCode.Y);
            LapMode = config.Bind("LapMode", false);
        }

        public override void Initialize()
        {
            base.Initialize();

            OpTab tabMain = new OpTab(this, Translate("Main"));
            Tabs = new[] { tabMain };

            options = new UIelement[]
            {
                new OpLabel(10f, 560f, Translate("Speedrun Race Mode Config"), true),
                new OpLabel(153f, 512f, Translate("Mercy button")) { alignment = FLabelAlignment.Left, description = Translate("Holding this while dying will allow the player to die properly.") },
                new OpKeyBinder(mercyButton, new Vector2(6f, 506f), new Vector2(140f, 20f), false, OpKeyBinder.BindController.AnyController) { description = Translate("Holding this while dying will allow the player to die properly.") },
                new OpLabel(153f, 470f, Translate("Lap Mode")) { alignment = FLabelAlignment.Left, description = Translate("Enables lap mode.") },
                new OpCheckBox(LapMode, new Vector2(6f, 464f)) { description = Translate("Enables lap mode, reset/restart timer upon entering starting room.") },
            };
            tabMain.AddItems(options);
        }
    }
}
