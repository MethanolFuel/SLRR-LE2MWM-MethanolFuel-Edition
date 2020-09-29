package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_MC_GTB_rear extends Swaybar
{
	public swaybar_MC_GTB_rear( int id )
	{
		super( id );

		force = 25000.0;
		damping = 2500.0;

		name_prefix = "MC GT-B rear";
		brand_prestige_factor = 1.90;
		calcStuffs();
	}
}

