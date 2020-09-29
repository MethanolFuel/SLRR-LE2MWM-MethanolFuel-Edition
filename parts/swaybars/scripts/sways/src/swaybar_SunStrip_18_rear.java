package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_SunStrip_18_rear extends Swaybar
{
	public swaybar_SunStrip_18_rear( int id )
	{
		super( id );

		force = 25000.0;
		damping = 2500.0;

		name_prefix = "Duhen SunStrip 1.8 DVC rear";
		brand_prestige_factor = 1.10;
		calcStuffs();
	}
}
