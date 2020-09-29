package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;


public class swaybar_Baiern_DS_front extends Swaybar
{
	public swaybar_Baiern_DS_front( int id )
	{
		super( id );

		force = 25000.0;
		damping = 2500.0;

		name_prefix = "Baiern DevilSport front";
		brand_prestige_factor = 1.60;
		calcStuffs();
	}
}

