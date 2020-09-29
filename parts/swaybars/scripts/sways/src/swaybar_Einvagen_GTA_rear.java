package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;

public class swaybar_Einvagen_GTA_rear extends Swaybar
{
	public swaybar_Einvagen_GTA_rear( int id )
	{
		super( id );

		force = 12500.0;
		damping = 1250.0;

		name_prefix = "Einvagen 140 GTA rear";
		brand_prestige_factor = 1.20;
		calcStuffs();
	}
}
