package java.game.parts.running_gear.sways;

import java.game.parts.rgearpart.*;

public class swaybar_Baiern_CS_front extends Swaybar
{
	public swaybar_Baiern_CS_front( int id )
	{
		super( id );

		force = 25000.0;
		damping = 2500.0;

		name_prefix = "Baiern CoupeSport 2.5 front";
		brand_prestige_factor = 1.20;
		calcStuffs();
	}
}

