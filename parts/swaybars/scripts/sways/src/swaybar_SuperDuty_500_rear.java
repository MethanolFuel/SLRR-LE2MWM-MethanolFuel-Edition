package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_SuperDuty_500_rear extends Swaybar
{
	public swaybar_SuperDuty_500_rear( int id )
	{
		super( id );

		force = 25000.0;
		damping = 2500.0;

		name_prefix = "Hauler's SuperDuty 500 rear";
		brand_prestige_factor = 1.55;
		calcStuffs();
	}
}

