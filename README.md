<p align="center">

  <h1 align="center">What to Distinguish and How? Opportunities and Challenges of Augmenting Multiple, Cluttered Objects in Complex Scenes for People with Low Vision</h1>
  <p align="center">
    Yuheng Wu<sup>1</sup>,
    Ruijia Chen<sup>1</sup>,
    Jaewook Lee<sup>2</sup>,
    Jia Li<sup>3</sup>,
    Kexin Zhang<sup>1</sup>,
    Meng Fong Lio<sup>1</sup>,
    Weibing Wang<sup>1</sup>, <br>
    Sanbrita Mondal<sup>1</sup>,
    Jon E. Froehlich<sup>2</sup>,
    Yapeng Tian<sup>3</sup>,
    Yuhang Zhao<sup>1</sup>
    <br><br>
    <sup>1</sup>University of Wisconsin-Madison,
    <sup>2</sup>University of Washington,
    <sup>3</sup>University of Texas at Dallas
    <br>
  </p>
  <h2 align="center">ASSETS 2026</h2>
  <h3 align="center"><a href="https://doi.org/10.1145/3797867.3828974">Paper (available after publish)</a> | <a href="./unity">Frontend</a> | <a href="./python">Backend</a> | <a href="https://github.com/MadisonAbilityLab/SceneGlance/releases/tag/v1.0">Fine-Tuned Models</a> | <a href="./Study%20Materials">Study Materials</a> | <a href="#citation">Citation</a></h3>
  <div align="center"></div>

</p>

<p align="center">
  <img src="assets/teaser.jpg" alt="This figure shows that SceneGlance augments and distinguishes multiple important objects in complex environments to support low vision people with scene perception. This figure has two subfigures. Subfigure A is SceneGlance augmenting multiple objects in a complex kitchen, with primary-important objects (glasses, knives) in yellow solid overlays and secondary-important objects (bowls, spoons, ladles) in blue outline. Subfigure B is SceneGlance augmenting multiple objects on a crowded street, with primary-important objects (curb, sewer drain, pedestrian signal, bicycles) in green outline with floating green icon labels, and secondary-important objects (sidewalk, pedestrian) in blue outline only." width="100%">
</p>

**SceneGlance** is a wearable augmented reality (AR) system that helps [people with low vision](https://www.nei.nih.gov/learn-about-eye-health/eye-conditions-and-diseases/low-vision) perceive complex, cluttered scenes such as a busy kitchen or a crowded street. Rather than enhancing the entire scene equally, which can add visual clutter and distraction, SceneGlance detects the objects that are important for people with low vision to perceive and augments them in 3D space. Its core idea is **AR distinction**: instead of augmenting every important object with the same augmentation, SceneGlance visually distinguishes them by their importance so the most critical ones stand out while reducing overall visual load. It offers three base augmentations (Static Outline, Solid Overlay, and Icon Labels) and three distinction methods (By Form, By Color, and By Additional Visual Information) that users can customize and combine to fit their preferences and visual needs. It contains a frontend on Microsoft HoloLens 2 to capture the scene, locate objects in 3D using its depth mesh, and render the augmentations; and a backend server to recognize the important objects with a fine-tuned RTMDet model.

In this repo, we present our [study scripts](./Study%20Materials) for the formative, indoor, and outdoor study. We also provide our implementation of the SceneGlance system, including the [frontend](./unity) and [backend](./python).

## Citation

> ⚠️ **Placeholder.** The official ASSETS 2026 citation (proceedings title, pages, DOI) will be
> added once the paper is published. Do not use the BibTeX below for formal citation yet.

If you use SceneGlance in your research, please cite our work:

```bibtex
@inproceedings{wu2026distinguish,
  title={What to Distinguish and How? Opportunities and Challenges of Augmenting Multiple, Cluttered Objects in Complex Scenes for People with Low Vision},
  author={Wu, Yuheng and Chen, Ruijia and Lee, Jaewook and Li, Jia and Zhang, Kexin and Lio, Meng Fong and Wang, Weibing and Mondal, Sanbrita and Froehlich, Jon E. and Tian, Yapeng and Zhao, Yuhang},
  booktitle={Proceedings of the ACM SIGACCESS Conference on Computers and Accessibility (ASSETS '26)},
  year={2026}
}
```

## Acknowledgements

We thank the University of Wisconsin-Madison Ability Lab, the University of Washington, and the University of Texas at Dallas. Special thanks to the accessibility community for their invaluable feedback and testing.

Codebase refactored for release with help from Claude.ai.

For questions and support, please open a GitHub Issue or contact Yuheng Wu ([iamwyh.com](https://iamwyh.com)) at the MadAbility Lab at UW-Madison.

## License

This project is licensed under the Creative Commons Attribution 4.0 International License (CC-BY-4.0). See the [LICENSE](LICENSE) file for details.

You are free to share and adapt this work for any purpose as long as you provide appropriate attribution to the original authors.
