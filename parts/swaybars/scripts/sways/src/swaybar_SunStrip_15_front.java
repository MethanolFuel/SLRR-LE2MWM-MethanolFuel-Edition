package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_SunStrip_15_front extends Swaybar
{
	public swaybar_SunStrip_15_front( int id )
	{
		super( id );

		force = 25000.0;
		damping = 2500.0;

		name_prefix = "Duhen SunStrip 1.5 DVC front";
		brand_prestige_factor = 1.00;
		calcStuffs();
	}
}
