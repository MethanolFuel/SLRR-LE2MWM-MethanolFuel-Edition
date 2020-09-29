package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_Focer_WRC_rear extends Swaybar
{
	public swaybar_Focer_WRC_rear( int id )
	{
		super( id );

		force = 25000.0;
		damping = 2500.0;

		name_prefix = "Shimutshibu Focer WRC rear";
		brand_prestige_factor = 3.50;
		calcStuffs();
	}
}

