package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_Focer_300_rear extends Swaybar
{
	public swaybar_Focer_300_rear( int id )
	{
		super( id );

		force = 25000.0;
		damping = 2500.0;

		name_prefix = "Shimutshibu Focer 300 rear";
		brand_prestige_factor = 1.39;
		calcStuffs();
	}
}

