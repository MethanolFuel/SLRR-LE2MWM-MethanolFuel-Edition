package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;

public class swaybar_Einvagen_GTA_front extends Swaybar
{
	public swaybar_Einvagen_GTA_front( int id )
	{
		super( id );

		force = 15500.0;
		damping = 1550.0;

		name_prefix = "Einvagen 140 GTA front";
		brand_prestige_factor = 1.20;
		calcStuffs();
	}
}
