package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_Einvagen_GT_front extends Swaybar
{
	public swaybar_Einvagen_GT_front( int id )
	{
		super( id );

		force = 7000.0;
		damping = 700.0;

		name_prefix = "Einvagen 110 GT front";
		brand_prestige_factor = 1.00;
		calcStuffs();
	}
}
