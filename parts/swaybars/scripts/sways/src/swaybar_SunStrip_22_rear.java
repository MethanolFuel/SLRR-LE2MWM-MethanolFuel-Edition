package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_SunStrip_22_rear extends Swaybar
{
	public swaybar_SunStrip_22_rear( int id )
	{
		super( id );

		force = 25000.0;
		damping = 2500.0;

		name_prefix = "Duhen SunStrip 2.2 DVC rear";
		brand_prestige_factor = 1.15;
		calcStuffs();
	}
}
