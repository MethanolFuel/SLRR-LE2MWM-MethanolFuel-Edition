package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_Baiern_GT_rear extends Swaybar
{
	public swaybar_Baiern_GT_rear( int id )
	{
		super( id );

		force = 25000.0;
		damping = 2500.0;

		name_prefix = "Baiern CoupeSport GT III rear";
		brand_prestige_factor = 4.00;
		calcStuffs();
	}
}

