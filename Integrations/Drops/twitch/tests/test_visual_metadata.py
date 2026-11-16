from __future__ import annotations
import sys,unittest
from pathlib import Path
from types import SimpleNamespace
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from visual_metadata import drop_visual,campaign_visual

class VisualMetadataTests(unittest.TestCase):
    def test_projects_only_existing_official_metadata(self):
        benefit=SimpleNamespace(id='benefit-a',name='Reward A',type=SimpleNamespace(value='DIRECT_ENTITLEMENT'),image_url='https://static-cdn.jtvnw.net/reward.png')
        campaign=SimpleNamespace(id='campaign-a',game=SimpleNamespace(name='Game A'),image_url='https://static-cdn.jtvnw.net/cover.jpg')
        drop=SimpleNamespace(benefits=[benefit],campaign=campaign)
        result=drop_visual(drop)
        self.assertEqual(result['benefits'][0]['imageUrl'],benefit.image_url)
        self.assertEqual(result['benefits'][0]['type'],'DIRECT_ENTITLEMENT')
        self.assertEqual(result['campaignId'],'campaign-a')
        self.assertEqual(result['benefits'][0]['id'],'benefit-a')
        self.assertEqual(campaign_visual(campaign)['gameImageUrl'],campaign.image_url)
        self.assertEqual(vars(drop),{'benefits':[benefit],'campaign':campaign})

    def test_old_objects_and_missing_art_remain_compatible(self):
        self.assertEqual(drop_visual(SimpleNamespace())['benefits'],[])
        self.assertEqual(drop_visual(SimpleNamespace(benefits=[SimpleNamespace(image_url='http://bad/unsafe')]))['benefits'][0]['imageUrl'],'')
        self.assertEqual(campaign_visual(SimpleNamespace())['gameImageUrl'],'')

    def test_metadata_never_invokes_business_properties(self):
        class ReadOnlyDrop:
            benefits=[]
            campaign=None
            @property
            def can_claim(self):raise AssertionError('not a visual field')
            def claim(self):raise AssertionError('must never claim')
        self.assertEqual(drop_visual(ReadOnlyDrop())['benefits'],[])

    def test_all_benefits_keep_identity_order_type_and_missing_image(self):
        benefits=[SimpleNamespace(id=str(n),name=f'item {n}',type=SimpleNamespace(value=kind),image_url=url) for n,kind,url in (
            (1,'DIRECT_ENTITLEMENT','https://static-cdn.jtvnw.net/one.png'),
            (2,'BADGE','https://static-cdn.jtvnw.net/two.png'),(3,'EMOTE',''))]
        result=drop_visual(SimpleNamespace(benefits=benefits))
        self.assertEqual(result['benefitIds'],['1','2','3'])
        self.assertEqual([b['type'] for b in result['benefits']],['DIRECT_ENTITLEMENT','BADGE','EMOTE'])
        self.assertEqual(result['benefits'][1]['imageUrl'],benefits[1].image_url)
        self.assertNotIn('imageUrl',result)  # No first-benefit-only DTO.

    def test_inventory_and_current_drop_share_full_readonly_snapshot(self):
        import json
        from datetime import datetime,timezone
        from worker import TwitchWorker
        campaign=SimpleNamespace(id='campaign-redacted',name='isolated campaign',game=SimpleNamespace(name='Game'),eligible=True,image_url='https://static-cdn.jtvnw.net/cover.jpg')
        benefits=[SimpleNamespace(id=f'benefit-{n}',name=f'isolated item {n}',type=SimpleNamespace(value='DIRECT_ENTITLEMENT'),image_url=f'https://static-cdn.jtvnw.net/{n}.png') for n in (1,2)]
        drop=SimpleNamespace(id='drop-redacted',name='isolated drop',campaign=campaign,benefits=benefits,real_current_minutes=18,extra_current_minutes=2,required_minutes=60,
            watch_requirement_completed=False,is_claimed=False,can_claim=False,precondition_drops=['prior-redacted'],preconditions_met=False,preconditions_satisfied_for_mining=True,
            starts_at=datetime(2026,1,1,tzinfo=timezone.utc),ends_at=datetime(2026,12,31,tzinfo=timezone.utc))
        worker=object.__new__(TwitchWorker);worker.running=True;worker._inventory_sync_at='';worker._client=SimpleNamespace(gui=SimpleNamespace(progress=SimpleNamespace(drop=drop)),_cloudlight_inventory_updated_at='2026-11-16T00:00:00Z')
        current=json.loads(json.dumps(worker._current_drop()))
        self.assertEqual(current,worker._drop_snapshot(drop))
        self.assertEqual(current['benefitIds'],['benefit-1','benefit-2'])
        self.assertEqual(current['campaignId'],'campaign-redacted')
        self.assertEqual(current['currentMinutes'],18);self.assertEqual(current['estimatedExtraMinutes'],2);self.assertEqual(current['progress'],.3)
        self.assertEqual(current['preconditionDropIds'],['prior-redacted']);self.assertFalse(current['preconditionsMet']);self.assertTrue(current['preconditionsSatisfiedForMining'])
        self.assertFalse(current['canClaim']);self.assertTrue(current['eligible'])

if __name__=='__main__':unittest.main()
