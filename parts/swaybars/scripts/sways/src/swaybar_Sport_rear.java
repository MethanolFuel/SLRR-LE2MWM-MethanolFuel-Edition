package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;

public class swaybar_Sport_rear extends Swaybar
{
	public swaybar_Sport_rear( int id )
	{
		super( id );

		force = 30000.0;
		damping = 3000.0;
		adjustable = 1;
		min_force = 35000.0;
		max_force = 25000.0;
		
		name_prefix = "Sport rear";
		brand_prestige_factor = 1.60;
		calcStuffs();
	}
}

