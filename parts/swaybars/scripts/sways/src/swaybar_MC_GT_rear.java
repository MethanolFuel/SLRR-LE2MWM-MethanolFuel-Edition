package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_MC_GT_rear extends Swaybar
{
	public swaybar_MC_GT_rear( int id )
	{
		super( id );

		force = 25000.0;
		damping = 2500.0;

		name_prefix = "MC GT rear";
		brand_prestige_factor = 2.00;
		calcStuffs();
	}
}

