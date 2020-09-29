package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_SunStrip_20_rear extends Swaybar
{
	public swaybar_SunStrip_20_rear( int id )
	{
		super( id );

		force = 25000.0;
		damping = 2500.0;

		name_prefix = "Duhen Racing SunStrip 2.0 CDVC rear";
		brand_prestige_factor = 2.25;
		calcStuffs();
	}
}
